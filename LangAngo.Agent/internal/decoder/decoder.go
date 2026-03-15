package decoder

import (
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"langango/agent/pkg/model"
	"time"
)

const (
	Magic1     byte = 0x4C
	Magic2     byte = 0x41
	Version    byte = 0x01
	HeaderSize      = 40
)

var (
	ErrInvalidMagic   = errors.New("invalid magic bytes")
	ErrInvalidVersion = errors.New("invalid version")
)

type BinaryDecoder struct{}

func NewBinaryDecoder() *BinaryDecoder {
	return &BinaryDecoder{}
}

func (d *BinaryDecoder) Decode(reader io.Reader) (*model.Span, error) {
	header := make([]byte, HeaderSize)
	n, err := io.ReadFull(reader, header)
	if err != nil {
		if n == 0 {
			return nil, io.EOF
		}
		return nil, fmt.Errorf("read header: got %d, err: %w", n, err)
	}

	if n != HeaderSize {
		return nil, errors.New("header truncated")
	}

	offset := 0

	if header[offset] != Magic1 || header[offset+1] != Magic2 {
		return nil, ErrInvalidMagic
	}
	offset += 2

	if header[offset] != Version {
		return nil, ErrInvalidVersion
	}
	offset++

	payloadType := model.PayloadType(header[offset])
	offset++

	payloadLen := binary.BigEndian.Uint32(header[offset : offset+4])
	offset += 4

	var traceID [16]byte
	copy(traceID[:], header[offset:offset+16])
	offset += 16

	spanID := binary.BigEndian.Uint64(header[offset : offset+8])
	offset += 8

	parentID := binary.BigEndian.Uint64(header[offset : offset+8])
	offset += 8

	span := &model.Span{
		Type:       payloadType,
		TraceID:    traceID,
		SpanID:     spanID,
		Kind:       model.SpanKindServer,
		StartTime:  time.Now(),
		Metadata:   make(map[string]string),
		Attributes: make(map[string]string),
	}

	if parentID != 0 {
		span.ParentID = &parentID
	}

	if payloadType == model.PayloadTypeException {
		span.Status = model.SpanStatusError
	}

	if payloadType == model.PayloadTypeEventPipe {
		return d.decodeEventPipeEvent(span, reader, payloadLen)
	}

	if payloadLen > 0 && payloadLen < 100000 {
		payload := make([]byte, payloadLen)
		n, err := io.ReadFull(reader, payload)
		if err != nil {
			return nil, fmt.Errorf("read payload: got %d, err: %w", n, err)
		}

		span = d.parsePayload(span, payload)
	}

	return span, nil
}

func (d *BinaryDecoder) decodeEventPipeEvent(span *model.Span, reader io.Reader, payloadLen uint32) (*model.Span, error) {
	if payloadLen == 0 {
		return span, nil
	}

	if payloadLen > 1024*1024 {
		return span, fmt.Errorf("payload too large: %d", payloadLen)
	}

	payload := make([]byte, payloadLen)
	n, err := io.ReadFull(reader, payload)
	if err != nil {
		return nil, fmt.Errorf("read eventpipe payload: got %d, err: %w", n, err)
	}

	if len(payload) < 9 {
		return span, nil
	}

	eventType := model.EventType(payload[0])
	offset := 1

	timestamp := binary.LittleEndian.Uint64(payload[offset : offset+8])
	offset += 8

	name := readString(payload, &offset)
	value := readString(payload, &offset)
	tags := readTags(payload, &offset)

	event := &model.EventPipeEvent{
		EventType: eventType,
		Timestamp: timestamp,
		Name:      name,
		Value:     value,
		Tags:      tags,
	}

	return event.ToSpan(), nil
}

func readString(data []byte, offset *int) string {
	if *offset+4 > len(data) {
		return ""
	}

	strLen := int(binary.LittleEndian.Uint32(data[*offset : *offset+4]))
	*offset += 4

	if strLen == 0 || *offset+strLen > len(data) {
		return ""
	}

	result := string(data[*offset : *offset+strLen])
	*offset += strLen
	return result
}

func readTags(data []byte, offset *int) map[string]string {
	if *offset+2 > len(data) {
		return nil
	}

	tagCount := int(binary.LittleEndian.Uint16(data[*offset : *offset+2]))
	*offset += 2

	if tagCount == 0 {
		return nil
	}

	tags := make(map[string]string, tagCount)
	for i := 0; i < tagCount; i++ {
		key := readString(data, offset)
		value := readString(data, offset)
		if key != "" {
			tags[key] = value
		}
	}

	return tags
}

func (d *BinaryDecoder) parsePayload(span *model.Span, payload []byte) *model.Span {
	if len(payload) < 4 {
		return span
	}

	nameLen := binary.LittleEndian.Uint32(payload[0:4])
	offset := 4

	if int(nameLen) <= len(payload)-offset && nameLen > 0 {
		span.Name = string(payload[offset : offset+int(nameLen)])
		offset += int(nameLen)
	}

	if offset+4 <= len(payload) {
		metadataLen := binary.LittleEndian.Uint32(payload[offset : offset+4])
		offset += 4

		if metadataLen > 0 && offset+int(metadataLen) <= len(payload) {
			metadataJSON := payload[offset : offset+int(metadataLen)]
			var metadata map[string]string
			if err := json.Unmarshal(metadataJSON, &metadata); err == nil {
				for k, v := range metadata {
					span.Metadata[k] = v
				}
			}
		}
	}

	return span
}
