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
    symbol             varchar(20)  not null,
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
    symbol        varchar(20) not null,
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
    symbol       varchar(20)     not null,
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

create unique index ix_candlesticks_symbol_market_timeframe_timestamp
    on candlesticks (symbol, market_type, timeframe, timestamp);

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
    symbol            varchar(20)     not null,
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

create index ix_orders_pipeline_id on orders (pipeline_id);
create index ix_orders_symbol on orders (symbol);
