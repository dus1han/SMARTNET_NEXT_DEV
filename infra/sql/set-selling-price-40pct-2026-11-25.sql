-- ---------------------------------------------------------------------------
-- Price every costed item at a 40% markup: selling_price = cost x 1.40, to the cent.
--
-- Asked for 2026-11-25. Applies to every live item (deleted_at IS NULL) with a cost above zero —
-- including the ones already at 40%, which come out unchanged. Items with no cost are left alone.
--
-- The new selling price is the List price every new quotation, proforma and invoice line can take.
-- Lines already on documents are not touched.
--
-- Each changed row gets updated_at and a row_version bump, so a screen holding the old version of an
-- item cannot save over this.
--
-- BACKUP. The old price of every row this changes is first copied into
--   archived_item_prices_20261125 (id, itemcode, cost, old_selling_price, new_selling_price)
--
-- UNDO:
--   UPDATE item_m i JOIN archived_item_prices_20261125 a ON a.id = i.id
--   SET i.selling_price = a.old_selling_price, i.updated_at = UTC_TIMESTAMP(6), i.row_version = i.row_version + 1;
--
-- RUN IT ON THE SERVER:
--   scp infra/sql/set-selling-price-40pct-2026-11-25.sql deploy@<host>:~/
--   sudo mysql smartnet_invsys < ~/set-selling-price-40pct-2026-11-25.sql
-- ---------------------------------------------------------------------------

-- Before.
SELECT COUNT(*) AS costed_items,
       SUM(selling_price IS NULL OR selling_price <> ROUND(cost * 1.40, 2)) AS will_change
FROM item_m
WHERE deleted_at IS NULL AND cost > 0;

CREATE TABLE IF NOT EXISTS archived_item_prices_20261125 (
  id                BIGINT        NOT NULL PRIMARY KEY,
  itemcode          VARCHAR(100)  NULL,
  cost              DECIMAL(18,4) NULL,
  old_selling_price DECIMAL(18,4) NULL,
  new_selling_price DECIMAL(18,4) NULL,
  archived_at       DATETIME(6)   NOT NULL
);

START TRANSACTION;

INSERT INTO archived_item_prices_20261125 (id, itemcode, cost, old_selling_price, new_selling_price, archived_at)
SELECT id, itemcode, cost, selling_price, ROUND(cost * 1.40, 2), UTC_TIMESTAMP(6)
FROM item_m
WHERE deleted_at IS NULL AND cost > 0
  AND (selling_price IS NULL OR selling_price <> ROUND(cost * 1.40, 2));

UPDATE item_m
SET selling_price = ROUND(cost * 1.40, 2),
    updated_at    = UTC_TIMESTAMP(6),
    row_version   = row_version + 1
WHERE deleted_at IS NULL AND cost > 0
  AND (selling_price IS NULL OR selling_price <> ROUND(cost * 1.40, 2));

COMMIT;

-- After: every costed item at exactly 40%.
SELECT COUNT(*) AS costed_items,
       SUM(selling_price = ROUND(cost * 1.40, 2)) AS at_40_percent,
       (SELECT COUNT(*) FROM archived_item_prices_20261125) AS changed_and_archived
FROM item_m
WHERE deleted_at IS NULL AND cost > 0;
