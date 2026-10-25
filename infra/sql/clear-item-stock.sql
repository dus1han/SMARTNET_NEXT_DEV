-- ---------------------------------------------------------------------------
-- Clear item stock — bring every item's stock balance to exactly zero.
--
-- Asked for 2026-10-25: every item, including any that have gone negative.
--
-- HOW STOCK WORKS HERE. A balance is never stored. It is the sum of the item's
-- rows in stock_movements, and that ledger is append-only — the app has no
-- edit or delete path for it. So "clearing" is done the way the app itself
-- corrects stock: one Adjustment movement per item, for the opposite of its
-- current balance. Nothing is edited or deleted; every item's history up to
-- today stays readable, followed by the line that zeroed it.
--
-- WHAT IT TOUCHES. Only stock_movements. item_stock (the legacy receipt
-- batches, shown for reference on the item's stock screen) is left alone —
-- the new app derives no balance from it.
--
-- MARKED. Every row this writes carries the reason
--   'Stock cleared to zero (bulk, 2026-10-25)'
-- so it can be found on each item's stock screen and by query.
--
-- RE-RUNNABLE. It only writes for items whose balance is not zero, so a second
-- run writes nothing.
--
-- UNDO. Run the counter-movement, not a delete:
--
--   INSERT INTO stock_movements (item_id, type, quantity, reason, occurred_at, created_at, row_version)
--   SELECT item_id, 'Adjustment', -quantity, 'Undo of bulk stock clear (2026-10-25)', UTC_TIMESTAMP(), UTC_TIMESTAMP(), 1
--   FROM stock_movements WHERE reason = 'Stock cleared to zero (bulk, 2026-10-25)';
--
-- RUN IT ON THE SERVER:
--
--   mysql -h <host> -u <user> -p <database> < clear-item-stock.sql
--
-- The first SELECT previews what will change; the last one should return no
-- rows, which is the proof every balance is now zero.
-- ---------------------------------------------------------------------------

-- 1. Preview: every item that currently holds (or owes) stock.
SELECT m.item_id, i.itemcode, i.itemname, SUM(m.quantity) AS balance_before
FROM stock_movements m
LEFT JOIN item_m i ON i.id = m.item_id
GROUP BY m.item_id, i.itemcode, i.itemname
HAVING SUM(m.quantity) <> 0
ORDER BY i.itemcode;

START TRANSACTION;

-- 2. One compensating adjustment per item, for exactly the opposite of its balance.
INSERT INTO stock_movements (item_id, type, quantity, reason, occurred_at, created_at, row_version)
SELECT b.item_id,
       'Adjustment',
       -b.balance,
       'Stock cleared to zero (bulk, 2026-10-25)',
       UTC_TIMESTAMP(),
       UTC_TIMESTAMP(),
       1
FROM (
    SELECT item_id, SUM(quantity) AS balance
    FROM stock_movements
    GROUP BY item_id
    HAVING SUM(quantity) <> 0
) AS b;

SELECT ROW_COUNT() AS items_cleared;

COMMIT;

-- 3. Verify: should return no rows.
SELECT item_id, SUM(quantity) AS balance_after
FROM stock_movements
GROUP BY item_id
HAVING SUM(quantity) <> 0;
