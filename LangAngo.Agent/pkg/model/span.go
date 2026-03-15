package model

import "time"

type PayloadType uint8

const (
	PayloadTypeSpan      PayloadType = 1
	PayloadTypeSymbol    PayloadType = 2
	PayloadTypeStack     PayloadType = 3
	PayloadTypeException PayloadType = 4
	PayloadTypeEventPipe PayloadType = 5
	PayloadTypeMetric    PayloadType = 6
	PayloadTypeSpanStart PayloadType = 7
	PayloadTypeSpanEnd   PayloadType = 8
)

type EventType uint8

const (
	EventTypeSpanStartEnd  EventType = 0x01
	EventTypeRuntimeMetric EventType = 0x02
	EventTypeException     EventType = 0x03
	EventTypeMetadata      EventType = 0x04
	EventTypeGC            EventType = 0x10
	EventTypeJIT           EventType = 0x11
	EventTypeThreadPool    EventType = 0x12
	EventTypeContention    EventType = 0x13
	EventTypeSampling      EventType = 0x20
)

type SpanKind uint8

const (
	SpanKindInternal SpanKind = 0
	SpanKindServer   SpanKind = 1
	SpanKindClient   SpanKind = 2
	SpanKindProducer SpanKind = 3
	SpanKindConsumer SpanKind = 4
)

type SpanStatus uint8

const (
	SpanStatusUnset SpanStatus = 0
	SpanStatusOk    SpanStatus = 1
	SpanStatusError SpanStatus = 2
)

type Span struct {
	Type          PayloadType
	TraceID       [16]byte
	SpanID        uint64
	ParentID      *uint64
	Kind          SpanKind
	Name          string
	ServiceName   string
	StartTime     time.Time
	EndTimestamp  *int64
	Status        SpanStatus
	StatusMessage *string
	Metadata      map[string]string
	Attributes    map[string]string
}

type EventPipeEvent struct {
	EventType    EventType
	Timestamp    uint64
	TraceID      [16]byte
	SpanID       uint64
	ParentSpanID *uint64
	Name         string
	Value        string
	Tags         map[string]string
}

func (e *EventPipeEvent) ToSpan() *Span {
	span := &Span{
		Type:       PayloadTypeEventPipe,
		TraceID:    e.TraceID,
		SpanID:     e.SpanID,
		ParentID:   e.ParentSpanID,
		Name:       e.Name,
		StartTime:  time.Now(),
		Metadata:   make(map[string]string),
		Attributes: make(map[string]string),
	}

	switch e.EventType {
	case EventTypeGC:
		span.Attributes["event_type"] = "gc"
		if e.Tags != nil {
			for k, v := range e.Tags {
				span.Attributes[k] = v
			}
		}
	case EventTypeJIT:
		span.Attributes["event_type"] = "jit"
		if e.Tags != nil {
			for k, v := range e.Tags {
				span.Attributes[k] = v
			}
		}
	case EventTypeThreadPool:
		span.Attributes["event_type"] = "threadpool"
		if e.Tags != nil {
			for k, v := range e.Tags {
				span.Attributes[k] = v
			}
		}
	case EventTypeContention:
		span.Attributes["event_type"] = "contention"
		if e.Tags != nil {
			for k, v := range e.Tags {
				span.Attributes[k] = v
			}
		}
	case EventTypeException:
		span.Attributes["event_type"] = "exception"
		span.Status = SpanStatusError
		if e.Value != "" {
			span.StatusMessage = &e.Value
		}
	case EventTypeRuntimeMetric:
		span.Attributes["event_type"] = "metric"
		span.Attributes["metric_name"] = e.Name
		span.Attributes["metric_value"] = e.Value
		if e.Tags != nil {
			for k, v := range e.Tags {
				span.Attributes[k] = v
			}
		}
	}

	if e.Tags != nil {
		for k, v := range e.Tags {
			span.Metadata[k] = v
		}
	}

	return span
}
