-- Takes the operations dashboard off the two administrator roles.
--
-- WHY
-- ---
-- Dev_Admin and Company_Admin were seeded from Permissions.All, which is the catalogue of everything
-- grantable — and that catalogue contains BOTH dashboards. Holding both is not a superset but a
-- contradiction: the operations dashboard is defined by what it withholds, so adding it to the
-- management one settles nothing and takes nothing away.
--
-- What it did settle was the permissions dialog, where the two are radio buttons. With both ticked the
-- last one wins, so an administrator's account showed "operations dashboard" selected.
--
-- The rendered dashboard was never wrong — the page prefers management when both are present — so this
-- is a display and data fault rather than anyone having seen the wrong figures. Nobody LOSES access:
-- both roles keep `dashboard`, the fuller of the two.
--
-- The seed no longer grants it (Permissions.AdministratorGrant), and RolesController now refuses a role
-- that holds both, so this repairs the past rather than guarding the future.
--
-- Idempotent: matches nothing once run. Safe to re-run.
--
-- SCOPE: the two system roles only. A custom role that deliberately grants the operations dashboard is
-- somebody's decision and none of this script's business.

DELETE rp
  FROM role_permissions rp
  JOIN roles r ON r.id = rp.role_id
 WHERE r.is_system = 1
   AND r.name IN ('Dev_Admin', 'Company_Admin')
   AND rp.permission = 'dashboard.operations';

-- Verification.
--   'still holds both' must return no rows.
--   Each administrator role must still hold `dashboard`.
SELECT CONCAT('still holds both: ', r.name) AS result
  FROM roles r
 WHERE r.is_system = 1
   AND (SELECT COUNT(*) FROM role_permissions rp
         WHERE rp.role_id = r.id
           AND rp.permission IN ('dashboard', 'dashboard.operations')) > 1
UNION ALL
SELECT CONCAT(r.name, ' -> ', rp.permission)
  FROM roles r
  JOIN role_permissions rp ON rp.role_id = r.id
 WHERE r.is_system = 1 AND rp.permission LIKE 'dashboard%';
