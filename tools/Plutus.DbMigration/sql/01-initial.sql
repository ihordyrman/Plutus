create table markets
(
    id         serial primary key,
    type       int          not null,
    api_key    varchar(500) not null,
    secret_key varchar(500) not null,
    passphrase varchar(500),
    is_sandbox boolean      not null default true,
    created_at timestamp    not null,
    updated_at timestamp    not null
);

create unique index ix_markets_type on markets (type);

create table pipelines
(
    id                 serial primary key,
    name               varchar(200) not null,
    instrument         varchar(20)  not null,
    market_type        int          not null,
    enabled            boolean      not null default false,
    execution_interval interval     not null,
    last_executed_at   timestamp,
    status             int          not null,
    tags               jsonb        not null default '[]',
    created_at         timestamp    not null,
    updated_at         timestamp    not null
);

create table positions
(
    id            serial primary key,
    pipeline_id   int references pipelines (id) on delete cascade,
    instrument    varchar(20) not null,
    status        int         not null,
    entry_price   decimal(28, 10),
    quantity      decimal(28, 10),
    buy_order_id  varchar(100),
    sell_order_id varchar(100),
    exit_price    decimal(28, 10),
    closed_at     timestamp,
    created_at    timestamp   not null,
    updated_at    timestamp   not null
);

create index ix_positions_pipeline_status on positions (pipeline_id, status);

create table candlesticks
(
    id           serial primary key,
    instrument   varchar(20)     not null,
    market_type  int             not null,
    timestamp    timestamp       not null,
    timeframe    varchar(10)     not null,
    open         decimal(28, 10) not null,
    high         decimal(28, 10) not null,
    low          decimal(28, 10) not null,
    close        decimal(28, 10) not null,
    volume       decimal(28, 10) not null,
    volume_quote decimal(28, 10) not null,
    is_completed boolean         not null default false
);

create unique index ix_candlesticks_instrument_market_timeframe_timestamp
    on candlesticks (instrument, market_type, timeframe, timestamp);

create index ix_candlesticks_timestamp on candlesticks (timestamp);

create table pipeline_steps
(
    id            serial primary key,
    pipeline_id   int references pipelines (id) on delete cascade,
    step_type_key varchar(100) not null,
    name          varchar(200) not null,
    "order"       int          not null,
    is_enabled    boolean      not null default true,
    parameters    jsonb        not null default '{}',
    created_at    timestamp    not null,
    updated_at    timestamp    not null,

    constraint uq_pipeline_steps_pipeline_order
        unique (pipeline_id, "order")
            deferrable initially immediate
);

create table orders
(
    id                serial primary key,
    pipeline_id       int references pipelines (id),
    market_type       int             not null,
    exchange_order_id varchar(100)    not null,
    instrument        varchar(20)     not null,
    side              int             not null,
    status            int             not null,
    quantity          decimal(28, 10) not null,
    price             decimal(28, 10),
    stop_price        decimal(28, 10),
    fee               decimal(28, 10),
    placed_at         timestamp,
    executed_at       timestamp,
    cancelled_at      timestamp,
    take_profit       decimal(28, 10),
    stop_loss         decimal(28, 10),
    created_at        timestamp       not null,
    updated_at        timestamp       not null
);

create index ix_orders_pipeline_id on orders (pipeline_id, created_at desc);
create index ix_orders_instrument on orders (instrument);
create index ix_orders_exchange_id_market on orders (exchange_order_id, market_type);
create index ix_orders_status on orders (status);

create table users
(
    id            serial primary key,
    username      varchar(100) not null unique,
    password_hash varchar(255) not null,
    created_at    timestamp    not null,
    updated_at    timestamp    not null
);

create table execution_logs
(
    id            serial primary key,
    pipeline_id   int references pipelines (id) on delete cascade,
    execution_id  varchar(100) not null,
    step_type_key varchar(100) not null,
    outcome       int          not null,
    message       text         not null,
    context       jsonb        not null default '{}',
    start_time    timestamp    not null,
    end_time      timestamp    not null
);

create index ix_execution_logs_pipeline_id on execution_logs (pipeline_id);
create index ix_execution_logs_execution_id on execution_logs (execution_id);

create table instruments
(
    id              serial primary key,
    instrument_id   varchar(50) not null,
    instrument_type varchar(20) not null,
    base_currency   varchar(20) not null,
    quote_currency  varchar(20) not null,
    market_type     int         not null,
    synced_at       timestamp   not null,
    created_at      timestamp   not null
);

create unique index ix_instruments_market_instrument on instruments (market_type, instrument_id);
create index ix_instruments_base_quote on instruments (market_type, instrument_type, base_currency, quote_currency);

create table candlestick_sync_jobs
(
    id              serial primary key,
    instrument      varchar(20) not null,
    market_type     int         not null,
    timeframe       varchar(10) not null,
    from_date       timestamptz not null,
    to_date         timestamptz not null,
    status          int         not null default 0,
    error_message   text,
    fetched_count   int         not null default 0,
    estimated_total int         not null default 0,
    current_cursor  timestamptz not null,
    started_at      timestamp   not null,
    last_update_at  timestamp   not null,
    created_at      timestamp   not null
);

CREATE TABLE backtest_runs
(
    id               serial PRIMARY KEY,
    pipeline_id      int             NOT NULL REFERENCES pipelines (id),
    status           int             NOT NULL DEFAULT 0,
    start_date       timestamp       NOT NULL,
    end_date         timestamp       NOT NULL,
    interval_minutes int             NOT NULL,
    initial_capital  decimal(28, 10) NOT NULL,
    final_capital    decimal(28, 10),
    total_trades     int             NOT NULL DEFAULT 0,
    win_rate         decimal(28, 10),
    max_drawdown     decimal(28, 10),
    sharpe_ratio     decimal(28, 10),
    error_message    text,
    created_at       timestamp       NOT NULL DEFAULT now(),
    completed_at     timestamp
);

CREATE INDEX idx_backtest_runs_pipeline_id ON backtest_runs (pipeline_id);
CREATE INDEX idx_backtest_runs_status ON backtest_runs (status);

CREATE TABLE backtest_trades
(
    id              serial PRIMARY KEY,
    backtest_run_id int             NOT NULL REFERENCES backtest_runs (id) ON DELETE CASCADE,
    side            int             NOT NULL,
    price           decimal(28, 10) NOT NULL,
    quantity        decimal(28, 10) NOT NULL,
    fee             decimal(28, 10) NOT NULL DEFAULT 0,
    candle_time     timestamp       NOT NULL,
    capital         decimal(28, 10) NOT NULL
);

CREATE INDEX idx_backtest_trades_run_id ON backtest_trades (backtest_run_id);

CREATE TABLE backtest_equity_points
(
    id              serial PRIMARY KEY,
    backtest_run_id int             NOT NULL REFERENCES backtest_runs (id) ON DELETE CASCADE,
    candle_time     timestamp       NOT NULL,
    equity          decimal(28, 10) NOT NULL,
    drawdown        decimal(28, 10) NOT NULL DEFAULT 0
);

CREATE INDEX idx_backtest_equity_run_id ON backtest_equity_points (backtest_run_id);

CREATE TABLE backtest_execution_logs
(
    id              serial PRIMARY KEY,
    backtest_run_id int       NOT NULL REFERENCES backtest_runs (id) ON DELETE CASCADE,
    execution_id    text      NOT NULL,
    step_type_key   text      NOT NULL,
    outcome         int       NOT NULL,
    message         text,
    context         text,
    candle_time     timestamp NOT NULL,
    start_time      timestamp NOT NULL,
    end_time        timestamp NOT NULL
);

CREATE INDEX idx_backtest_exec_logs_run_id ON backtest_execution_logs (backtest_run_id);
CREATE INDEX idx_backtest_exec_logs_execution ON backtest_execution_logs (backtest_run_id, execution_id);

CREATE TABLE api_keys
(
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL,
    key_hash   VARCHAR(255) NOT NULL,
    key_prefix VARCHAR(12)  NOT NULL,
    is_active  BOOLEAN      NOT NULL DEFAULT true,
    last_used  TIMESTAMP,
    created_at TIMESTAMP    NOT NULL DEFAULT now()
);
CREATE INDEX ix_api_keys_hash ON api_keys (key_hash);
