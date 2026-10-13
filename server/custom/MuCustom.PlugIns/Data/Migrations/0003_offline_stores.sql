-- Tiendas offline persistentes: sobreviven a un reinicio o caída del servidor.
-- Una fila por tienda abierta; al cerrarse pasa al historial (con lo vendido) y se borra.

CREATE TABLE IF NOT EXISTS offline_stores (
    character_id   uuid        PRIMARY KEY REFERENCES data."Character"("Id") ON DELETE CASCADE,
    account_id     uuid        NOT NULL REFERENCES data."Account"("Id") ON DELETE CASCADE,
    login_name     text        NOT NULL,
    character_name text        NOT NULL,
    store_name     text        NOT NULL,
    -- El servidor de juego donde está la tienda: al arrancar, cada servidor restaura las suyas.
    server_id      smallint    NOT NULL,
    stays_on_login boolean     NOT NULL,
    started_at     timestamptz NOT NULL DEFAULT now(),
    -- Vencimiento absoluto (NULL = sin límite): el tiempo con el servidor apagado también cuenta.
    expires_at     timestamptz,
    sold_items     integer     NOT NULL DEFAULT 0,
    sold_zen       bigint      NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_offline_stores_server ON offline_stores (server_id);
CREATE INDEX IF NOT EXISTS ix_offline_stores_account ON offline_stores (account_id);

CREATE TABLE IF NOT EXISTS offline_store_history (
    id             bigserial   PRIMARY KEY,
    character_id   uuid        NOT NULL REFERENCES data."Character"("Id") ON DELETE CASCADE,
    account_id     uuid        NOT NULL REFERENCES data."Account"("Id") ON DELETE CASCADE,
    character_name text        NOT NULL,
    store_name     text        NOT NULL,
    started_at     timestamptz NOT NULL,
    closed_at      timestamptz NOT NULL DEFAULT now(),
    reason         text        NOT NULL,
    sold_items     integer     NOT NULL,
    sold_zen       bigint      NOT NULL,
    -- El dueño ya vio el resumen al entrar al juego.
    notified       boolean     NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS ix_offline_store_history_pending ON offline_store_history (account_id) WHERE NOT notified;
