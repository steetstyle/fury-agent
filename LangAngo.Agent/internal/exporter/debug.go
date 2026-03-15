package exporter

import (
	"fmt"
	"langango/agent/pkg/model"
	"strings"
)

type DebugExporter struct {
	pretty bool
	debug  bool
	spans  []*model.Span
}

func NewDebugExporter(pretty, debug bool) *DebugExporter {
	return &DebugExporter{
		pretty: pretty,
		debug:  debug,
		spans:  make([]*model.Span, 0),
	}
}

func (e *DebugExporter) Export(span *model.Span) {
	e.spans = append(e.spans, span)

	if e.pretty {
		e.printPretty(span)
	} else {
		fmt.Printf("Span: %s | %s\n", span.Name, span.Traceparent())
	}
}

func (e *DebugExporter) printPretty(span *model.Span) {
	traceID := fmt.Sprintf("%x", span.TraceID)
	spanID := fmt.Sprintf("%x", span.SpanID)

	if e.pretty {
		fmt.Printf("┌─ ✓ %s\n", span.Name)
		fmt.Printf("│  TraceID: %s\n", traceID)
		fmt.Printf("│  SpanID:  %s\n", spanID)
		fmt.Printf("│  traceparent: %s\n", span.Traceparent())
		fmt.Printf("│  Type:    %d | Kind: %d | Status: %d\n", span.Type, span.Kind, span.Status)

		if span.ServiceName != "" {
			fmt.Printf("│  Service: %s\n", span.ServiceName)
		}

		if len(span.Attributes) > 0 {
			fmt.Printf("│  ── Attributes ──\n")
			for k, v := range span.Attributes {
				if len(v) > 100 {
					v = v[:100] + "..."
				}
				fmt.Printf("│    %s: %s\n", k, v)
			}
		}

		if len(span.Metadata) > 0 {
			fmt.Printf("│  ── Metadata ──\n")
			for k, v := range span.Metadata {
				if len(v) > 100 {
					v = v[:100] + "..."
				}
				fmt.Printf("│    %s: %s\n", k, v)
			}
		}
		fmt.Printf("└\n")
	} else {
		fmt.Printf("[%s] %s Kind: %d\n", span.Name, span.Traceparent(), span.Kind)
	}
}

func (e *DebugExporter) PrintSummary(stats *ExportStats) {
	fmt.Printf("\n=== Summary ===\n")
	fmt.Printf("Total spans: %d\n", len(e.spans))

	byName := make(map[string]int)
	for _, s := range e.spans {
		byName[s.Name]++
	}
	fmt.Printf("Spans by name:\n")
	for name, count := range byName {
		fmt.Printf("  %s: %d\n", name, count)
	}
}

type ExportStats struct {
	totalSpans  int
	totalErrors int
	spansByType map[model.PayloadType]int
}

func NewExportStats() *ExportStats {
	return &ExportStats{
		spansByType: make(map[model.PayloadType]int),
	}
}

func (s *ExportStats) Add(span *model.Span) {
	s.totalSpans++
	s.spansByType[span.Type]++

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

type JaegerExporter struct {
	endpoint string
	service  string
}

func NewJaegerExporter(endpoint, service string) *JaegerExporter {
	return &JaegerExporter{
		endpoint: endpoint,
		service:  service,
	}
}

func (e *JaegerExporter) Export(span *model.Span) {
	_ = e.endpoint
	_ = e.service

	var parentID string
	if span.ParentID != nil {
		parentID = fmt.Sprintf("%x", *span.ParentID)
	}

	traceID := fmt.Sprintf("%x", span.TraceID)
	spanID := fmt.Sprintf("%x", span.SpanID)

	_ = parentID
	_ = traceID
	_ = spanID
}

type ConsoleExporter struct{}

func NewConsoleExporter() *ConsoleExporter {
	return &ConsoleExporter{}
}

func (e *ConsoleExporter) Export(span *model.Span) {
	var sb strings.Builder
	sb.WriteString(fmt.Sprintf("Span: %s\n", span.Name))
	sb.WriteString(fmt.Sprintf("  traceparent: %s\n", span.Traceparent()))
	sb.WriteString(fmt.Sprintf("  Kind: %v\n", span.Kind))
	sb.WriteString(fmt.Sprintf("  Status: %v\n", span.Status))

	if len(span.Metadata) > 0 {
		sb.WriteString("  Metadata:\n")
		for k, v := range span.Metadata {
			sb.WriteString(fmt.Sprintf("    %s: %s\n", k, v))
		}
	}

	fmt.Print(sb.String())
}
