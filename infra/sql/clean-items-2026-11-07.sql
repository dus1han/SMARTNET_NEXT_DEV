-- ---------------------------------------------------------------------------
-- Item clean-up — delete every item no document uses; zero the stock of the rest.
--
-- Asked for 2026-11-07:
--   A. items on no document and with no stock          -> delete
--   B. items on no document but with stock entered      -> delete (the stock is not wanted)
--   C. items on at least one document                   -> keep, stock brought to exactly 0
--
-- "ON A DOCUMENT" means referenced, by item_m.id or by its non-blank itemcode, from any line table:
-- invoice_l, invoice_l_old, del_invoice_l, archived_orphaned_invoice_l, quotation_l,
-- archived_orphaned_quotation_l, proforma_invoice_lines, cn_l, del_cn_l, po_l, jobcard_l, and the legacy
-- stock batches (item_stock). An item picked in a saved draft (document_drafts) is also kept, so a draft
-- cannot be left pointing at nothing.
--
-- The groups are worked out WHEN THIS RUNS, not from a list taken earlier — an item used on an invoice
-- between the review and the run is kept.
--
-- DELETES ARE REAL (item_m rows go, from the new app and the legacy app alike), but nothing is lost:
-- every deleted item row and every stock movement deleted with it is first copied into
--   archived_items_20261107
--   archived_stock_movements_20261107
-- stock_movements has a foreign key to item_m, so a B item's movements must go before it can.
--
-- ZEROING works the way the app corrects stock: one Adjustment movement per C item, for the opposite of
-- its balance, marked with the reason 'Stock cleared to zero (bulk, 2026-11-07)'. Nothing is edited.
--
-- UNDO:
--   INSERT INTO item_m SELECT * FROM archived_items_20261107;
--   INSERT INTO stock_movements SELECT * FROM archived_stock_movements_20261107;
--   INSERT INTO stock_movements (item_id, type, quantity, reason, occurred_at, created_at, row_version)
--   SELECT item_id, 'Adjustment', -quantity, 'Undo of bulk stock clear (2026-11-07)', UTC_TIMESTAMP(), UTC_TIMESTAMP(), 1
--   FROM stock_movements WHERE reason = 'Stock cleared to zero (bulk, 2026-11-07)';
--
-- RUN IT ON THE SERVER:
--   scp infra/sql/clean-items-2026-11-07.sql deploy@<host>:~/
--   sudo mysql smartnet_invsys < ~/clean-items-2026-11-07.sql
-- ---------------------------------------------------------------------------

-- Every line that names an item, by id or by code.
DROP TEMPORARY TABLE IF EXISTS refs;
CREATE TEMPORARY TABLE refs (item_id BIGINT NULL, itemcode VARCHAR(200) NULL, KEY (item_id), KEY (itemcode));
INSERT INTO refs SELECT item_id, itemcode FROM invoice_l;
INSERT INTO refs SELECT NULL, itemcode FROM invoice_l_old;
INSERT INTO refs SELECT NULL, itemcode FROM del_invoice_l;
INSERT INTO refs SELECT item_id, itemcode FROM archived_orphaned_invoice_l;
INSERT INTO refs SELECT item_id, itemcode FROM quotation_l;
INSERT INTO refs SELECT item_id, itemcode FROM archived_orphaned_quotation_l;
INSERT INTO refs SELECT item_id, item_code FROM proforma_invoice_lines;
INSERT INTO refs SELECT item_id, itemcode FROM cn_l;
INSERT INTO refs SELECT NULL, itemcode FROM del_cn_l;
INSERT INTO refs SELECT item_id, item_code FROM po_l;
INSERT INTO refs SELECT item_id, NULL FROM jobcard_l;
INSERT INTO refs SELECT NULL, item_code FROM item_stock;

-- Each live item, with whether anything uses it and its derived stock balance.
DROP TEMPORARY TABLE IF EXISTS item_class;
CREATE TEMPORARY TABLE item_class (PRIMARY KEY (id)) AS
SELECT i.id,
       i.itemcode,
       EXISTS (SELECT 1 FROM refs r
               WHERE r.item_id = i.id
                  OR (i.itemcode IS NOT NULL AND i.itemcode <> '' AND r.itemcode = i.itemcode))
       OR EXISTS (SELECT 1 FROM document_drafts d
                  WHERE d.payload REGEXP CONCAT('"itemId":', i.id, '[^0-9]')) AS used,
       (SELECT COALESCE(SUM(m.quantity), 0) FROM stock_movements m WHERE m.item_id = i.id) AS balance
FROM item_m i
WHERE i.deleted_at IS NULL;

DROP TEMPORARY TABLE IF EXISTS to_delete;
CREATE TEMPORARY TABLE to_delete (PRIMARY KEY (id)) AS SELECT id FROM item_class WHERE used = 0;

DROP TEMPORARY TABLE IF EXISTS to_zero;
CREATE TEMPORARY TABLE to_zero (PRIMARY KEY (id)) AS SELECT id, balance FROM item_class WHERE used = 1 AND balance <> 0;

-- Before: what is about to happen.
-- (Separate statements: MySQL cannot open one temporary table twice in a single query.)
SELECT COUNT(*) AS items_to_delete FROM to_delete;
SELECT COUNT(DISTINCT m.item_id) AS of_which_with_stock_movements FROM stock_movements m JOIN to_delete d ON d.id = m.item_id;
SELECT COUNT(*) AS items_kept_on_a_document FROM item_class WHERE used = 1;
SELECT COUNT(*) AS of_which_stock_to_zero FROM to_zero;

-- The archives (DDL, so outside the transaction — CREATE TABLE commits implicitly). LIKE copies the
-- columns and keys but not the foreign key, so archived movements do not pin anything.
CREATE TABLE IF NOT EXISTS archived_items_20261107 LIKE item_m;
CREATE TABLE IF NOT EXISTS archived_stock_movements_20261107 LIKE stock_movements;

START TRANSACTION;

INSERT INTO archived_stock_movements_20261107
SELECT m.* FROM stock_movements m JOIN to_delete d ON d.id = m.item_id;

INSERT INTO archived_items_20261107
SELECT i.* FROM item_m i JOIN to_delete d ON d.id = i.id;

DELETE m FROM stock_movements m JOIN to_delete d ON d.id = m.item_id;
DELETE i FROM item_m i JOIN to_delete d ON d.id = i.id;

INSERT INTO stock_movements (item_id, type, quantity, reason, occurred_at, created_at, row_version)
SELECT id, 'Adjustment', -balance, 'Stock cleared to zero (bulk, 2026-11-07)', UTC_TIMESTAMP(), UTC_TIMESTAMP(), 1
FROM to_zero;

COMMIT;

-- After: should read 0 deleted items left, the archive counts, and 0 kept items with stock.
SELECT 'deleted items still in item_m' AS what, COUNT(*) AS n FROM item_m i JOIN to_delete d ON d.id = i.id
UNION ALL SELECT 'archived items', COUNT(*) FROM archived_items_20261107
UNION ALL SELECT 'archived stock movements', COUNT(*) FROM archived_stock_movements_20261107
UNION ALL SELECT 'live items left', COUNT(*) FROM item_m WHERE deleted_at IS NULL
UNION ALL SELECT 'live items with non-zero stock',
       COUNT(*) FROM (SELECT m.item_id FROM stock_movements m JOIN item_m i ON i.id = m.item_id AND i.deleted_at IS NULL
                      GROUP BY m.item_id HAVING SUM(m.quantity) <> 0) AS s;
