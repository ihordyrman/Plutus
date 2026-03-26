```
- `[Rectangle]` - data/records
- `((Circle))` - events
- `>Asymmetric]` - external trigger
```

# Market Data Synchronization

```mermaid
graph LR
	A>Periodic Timer] --> B((SyncStarted))
	B --> C[Active Instruments]
	C --> D((CandlesticksExtracted))
	D --> E[New Candlesticks]
	E --> F((CandlesticksSaved))
```

Events: `SyncStarted`, `CandlestickExtracted`, `CandlestickSaved`
Data records: `Active Instruments`, `New Candlesticks`
