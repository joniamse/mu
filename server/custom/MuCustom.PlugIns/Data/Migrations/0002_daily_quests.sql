-- FASE 2: Misiones diarias. El ciclo es el dia UTC (reinicio a las 00:00 UTC).

CREATE TABLE IF NOT EXISTS openmu_quests (
    id             serial   PRIMARY KEY,
    code           text     NOT NULL UNIQUE,
    title          text     NOT NULL,
    description    text     NOT NULL DEFAULT '',
    -- NULL = cualquier mapa / cualquier monstruo.
    map_number     smallint,
    monster_number smallint,
    required_count integer  NOT NULL CHECK (required_count > 0),
    min_level      integer  NOT NULL DEFAULT 1,
    reward_zen     integer  NOT NULL DEFAULT 0 CHECK (reward_zen >= 0),
    reward_exp     integer  NOT NULL DEFAULT 0 CHECK (reward_exp >= 0),
    reward_fichas  integer  NOT NULL DEFAULT 0 CHECK (reward_fichas >= 0),
    sort_order     integer  NOT NULL DEFAULT 0,
    active         boolean  NOT NULL DEFAULT true
);

CREATE TABLE IF NOT EXISTS openmu_quest_progress (
    character_id uuid        NOT NULL REFERENCES data."Character"("Id") ON DELETE CASCADE,
    quest_id     integer     NOT NULL REFERENCES openmu_quests(id) ON DELETE CASCADE,
    period_date  date        NOT NULL,
    progress     integer     NOT NULL DEFAULT 0 CHECK (progress >= 0),
    claimed_at   timestamptz,
    updated_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (character_id, quest_id, period_date)
);
CREATE INDEX IF NOT EXISTS ix_openmu_quest_progress_period ON openmu_quest_progress (period_date);

INSERT INTO openmu_quests (code, title, description, map_number, monster_number, required_count, min_level, reward_zen, reward_exp, reward_fichas, sort_order) VALUES
    ('lorencia_spiders',   'Plaga de Lorencia',       'Las arañas se acercan a la ciudad. Elimina 50 Spiders en Lorencia.',              0,    3,  50,   1,  100000,   20000,  5, 10),
    ('lorencia_budges',    'Alas de fuego',           'Caza 40 Budge Dragons en Lorencia.',                                              0,    2,  40,   1,  150000,   30000,  5, 20),
    ('noria_goblins',      'El bosque de Noria',      'Los goblins saquean las caravanas. Elimina 50 Goblins en Noria.',                 3,   26,  50,  20,  300000,   80000, 10, 30),
    ('dungeon_skeletons',  'Huesos inquietos',        'Devuelve al polvo a 60 Skeleton Warriors en Dungeon.',                            1,   14,  60,  40,  500000,  150000, 10, 40),
    ('devias_yetis',       'Hielo y colmillos',       'Abate 60 Yetis en Devias.',                                                       2,   19,  60,  50,  600000,  200000, 10, 50),
    ('losttower_dk',       'Caballeros de la Torre',  'Los Death Knights custodian Lost Tower. Derrota a 50 de ellos.',                  4,   40,  50, 100, 1500000,  600000, 20, 60),
    ('global_hunter',      'Exterminador',            'Elimina 300 monstruos en cualquier mapa.',                                     NULL, NULL, 300,   1,  500000,  100000, 15, 90)
ON CONFLICT (code) DO NOTHING;
