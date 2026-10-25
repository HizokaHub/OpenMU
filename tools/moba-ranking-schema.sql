-- Esquema aditivo del ranking de MOBA (2026-10-26). NO se aplica solo: ejecutar a mano DESPUES de un pg_dump y con confirmacion.
--   pg_dump -U postgres -h localhost -Fc -f backup_antes_ranking.dump openmu
--   psql -U postgres -h localhost -d openmu -f tools/moba-ranking-schema.sql
-- Para deshacerlo:   drop schema moba cascade;
-- No toca ninguna tabla del juego (esquemas data, config, guild, friend, public).

create schema if not exists moba;

-- Una fila por partida terminada.
create table if not exists moba.match
(
    id               bigserial primary key,
    ended_at         timestamptz not null default now(),
    duration_seconds integer     not null,
    winner_team      smallint    not null,           -- 1 azul, 2 rojo (MobaTeam)
    format           text        not null default '',
    golden_invasion  boolean     not null default false
);

-- Una fila por campeon de cada partida (humanos y bots; los bots no cuentan para el ranking).
create table if not exists moba.match_player
(
    match_id       bigint   not null references moba.match (id) on delete cascade,
    slot           smallint not null,
    account_login  text     null,                    -- solo interno: no se publica en la web
    character_name text     not null,
    class_index    smallint not null,               -- 0 Mago, 1 Caballero, 2 Elfa, 3 Gladiador, 4 Dark Lord, 5 Invocador, 6 Rage Fighter
    team           smallint not null,
    is_bot         boolean  not null,
    won            boolean  not null,
    level          smallint not null,
    kills          integer  not null,
    deaths         integer  not null,
    assists        integer  not null,
    creep_score    integer  not null,
    gold_earned    integer  not null,
    damage_dealt   bigint   not null,
    damage_taken   bigint   not null,
    golden_kills   integer  not null default 0,
    primary key (match_id, slot)
);

create index if not exists ix_match_player_name on moba.match_player (character_name);
create index if not exists ix_match_player_account on moba.match_player (account_login);

-- Estado del servidor, escrito por el game server cada pocos segundos (la web lo lee).
create table if not exists moba.server_status
(
    source     text primary key,
    players    integer     not null,
    in_match   integer     not null,
    updated_at timestamptz not null default now()
);

-- Estadisticas por personaje (solo humanos). Los puntos del ranking: 25 por victoria + 5 por partida + 1 por kill + 0,5 por asistencia.
create or replace view moba.player_stats as
select
    character_name,
    (array_agg(class_index order by ended_at desc))[1]                      as class_index,
    count(*)                                                                as matches,
    count(*) filter (where won)                                             as wins,
    sum(kills)                                                              as kills,
    sum(deaths)                                                             as deaths,
    sum(assists)                                                            as assists,
    sum(creep_score)                                                        as creep_score,
    max(level)                                                              as best_level,
    sum(golden_kills)                                                       as golden_kills,
    max(ended_at)                                                           as last_match,
    (25 * count(*) filter (where won)) + (5 * count(*)) + sum(kills) + (0.5 * sum(assists)) as points
from moba.match_player p
join moba.match m on m.id = p.match_id
where not p.is_bot
group by character_name;
