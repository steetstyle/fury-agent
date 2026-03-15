package processor

import (
	"langango/agent/pkg/model"
)

type Processor interface {
	Process(span *model.Span)
	Flush()
}

type SpanProcessor struct {
	exporter Exporter
	stats    *ExportStats
}

func NewProcessor(exp Exporter) *SpanProcessor {
	return &SpanProcessor{
		exporter: exp,
		stats:    NewExportStats(),
	}
}

func NewProcessorWithStats(exp Exporter, stats *ExportStats) *SpanProcessor {
	return &SpanProcessor{
		exporter: exp,
		stats:    stats,
	}
}

func (p *SpanProcessor) Process(span *model.Span) {
	p.stats.Add(span)
	p.exporter.Export(span)
}

func (p *SpanProcessor) Flush() {
}

type ExportStats struct {
	totalSpans  int
	totalErrors int
}

func NewExportStats() *ExportStats {
	return &ExportStats{}
}

func (s *ExportStats) Add(span *model.Span) {
	s.totalSpans++
	if span.Status == model.SpanStatusError {
		s.totalErrors++
	}
}

func (s *ExportStats) Total() int {
	return s.totalSpans
}

func (s *ExportStats) Errors() int {
	return s.totalErrors
}

type Exporter interface {
	Export(span *model.Span)
}

type NoopExporter struct{}

func NewNoopExporter() *NoopExporter {
	return &NoopExporter{}
}

func (e *NoopExporter) Export(span *model.Span) {
}
