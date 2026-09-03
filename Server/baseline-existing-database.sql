-- Jumping Ninja one-time baseline for a database created by EnsureCreated.
-- Run this only after taking a PostgreSQL backup and verifying the database
-- belongs to this installation. It never creates, drops, or alters Identity
-- tables; it only records the already-present schema as IdentityBaseline so
-- the next application start can apply AddOnlineLeaderboard.
\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    required_table text;
BEGIN
    FOREACH required_table IN ARRAY ARRAY[
        'AspNetUsers',
        'AspNetRoles',
        'AspNetRoleClaims',
        'AspNetUserClaims',
        'AspNetUserLogins',
        'AspNetUserRoles',
        'AspNetUserTokens'
    ] LOOP
        IF to_regclass(format('%I', required_table)) IS NULL THEN
            RAISE EXCEPTION
                'Cannot baseline this database: required Identity table % is missing.',
                required_table;
        END IF;
    END LOOP;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND table_name = 'AspNetUsers'
          AND column_name = 'Id'
    ) OR NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND table_name = 'AspNetUsers'
          AND column_name = 'PasswordHash'
    ) THEN
        RAISE EXCEPTION
            'Cannot baseline this database: AspNetUsers does not match the expected Identity schema.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260902141820_IdentityBaseline', '10.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
