-- FASE 1: Fichas (moneda premium), paquetes de recarga y membresias VIP.
-- Las tablas viven en el schema public de la base de OpenMU y referencian sus cuentas y personajes.

CREATE TABLE IF NOT EXISTS account_cash_currency (
    account_id      uuid        PRIMARY KEY REFERENCES data."Account"("Id") ON DELETE CASCADE,
    fichas_balance  bigint      NOT NULL DEFAULT 0 CHECK (fichas_balance >= 0),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

-- Cada movimiento de fichas (recarga, compra, recompensa, ajuste) queda registrado.
CREATE TABLE IF NOT EXISTS fichas_ledger (
    id            bigserial   PRIMARY KEY,
    account_id    uuid        NOT NULL REFERENCES data."Account"("Id") ON DELETE CASCADE,
    delta         bigint      NOT NULL,
    balance_after bigint      NOT NULL,
    reason        text        NOT NULL,
    reference     text,
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_fichas_ledger_account ON fichas_ledger (account_id, created_at DESC);

CREATE TABLE IF NOT EXISTS shop_recharge_packages (
    id           smallint      PRIMARY KEY,
    code         text          NOT NULL UNIQUE,
    name         text          NOT NULL,
    price_usd    numeric(10,2) NOT NULL CHECK (price_usd > 0),
    base_fichas  integer       NOT NULL CHECK (base_fichas > 0),
    bonus_fichas integer       NOT NULL DEFAULT 0 CHECK (bonus_fichas >= 0),
    total_fichas integer       GENERATED ALWAYS AS (base_fichas + bonus_fichas) STORED,
    active       boolean       NOT NULL DEFAULT true
);

INSERT INTO shop_recharge_packages (id, code, name, price_usd, base_fichas, bonus_fichas) VALUES
    (1, 'basico',       'Básico',          5.00,   500,    0),
    (2, 'aventurero',   'Aventurero',     10.00,  1000,  100),
    (3, 'elite',        'Élite',          15.00,  1500,  200),
    (4, 'maestro',      'Maestro',        25.00,  2500,  500),
    (5, 'conquistador', 'Conquistador',   50.00,  5000, 1500),
    (6, 'leyenda',      'Leyenda',       100.00, 10000, 4000)
ON CONFLICT (id) DO NOTHING;

-- Diamante (nivel 4) es de cuenta: aplica a todos los personajes.
CREATE TABLE IF NOT EXISTS account_vip_status (
    account_id  uuid        PRIMARY KEY REFERENCES data."Account"("Id") ON DELETE CASCADE,
    vip_level   smallint    NOT NULL CHECK (vip_level BETWEEN 0 AND 4),
    expires_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- Plata, Oro y Platino (niveles 1 a 3) son de un personaje.
CREATE TABLE IF NOT EXISTS character_vip_status (
    character_id uuid        PRIMARY KEY REFERENCES data."Character"("Id") ON DELETE CASCADE,
    account_id   uuid        NOT NULL REFERENCES data."Account"("Id") ON DELETE CASCADE,
    vip_level    smallint    NOT NULL CHECK (vip_level BETWEEN 0 AND 4),
    expires_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_character_vip_status_account ON character_vip_status (account_id);

-- Baules extra (/baul 1-5). El 1 es el baul original de la cuenta.
CREATE TABLE IF NOT EXISTS account_vaults (
    account_id      uuid     NOT NULL REFERENCES data."Account"("Id") ON DELETE CASCADE,
    vault_no        smallint NOT NULL CHECK (vault_no BETWEEN 1 AND 5),
    item_storage_id uuid     NOT NULL UNIQUE,
    PRIMARY KEY (account_id, vault_no)
);

CREATE TABLE IF NOT EXISTS account_vault_state (
    account_id      uuid     PRIMARY KEY REFERENCES data."Account"("Id") ON DELETE CASCADE,
    active_vault_no smallint NOT NULL DEFAULT 1 CHECK (active_vault_no BETWEEN 1 AND 5)
);

-- Entradas diarias de Blood Castle / Devil Square incluidas en la membresia (/entradas).
CREATE TABLE IF NOT EXISTS vip_event_entry_claims (
    character_id uuid     NOT NULL REFERENCES data."Character"("Id") ON DELETE CASCADE,
    period_date  date     NOT NULL,
    claimed      smallint NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, period_date)
);

-- Saldo inicial en 0 para las cuentas existentes; las nuevas lo reciben al entrar al juego.
INSERT INTO account_cash_currency (account_id)
SELECT "Id" FROM data."Account"
ON CONFLICT (account_id) DO NOTHING;
