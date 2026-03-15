package receiver

import (
	"fmt"
	"net"
	"sync"
	"time"
)

type Receiver struct {
	addr string
	ln   net.Listener
	wg   sync.WaitGroup
	done chan struct{}
}

func New(addr string) *Receiver {
	return &Receiver{
		addr: addr,
		done: make(chan struct{}),
	}
}

func (r *Receiver) Start(handler func(conn net.Conn)) error {
	ln, err := net.Listen("unix", r.addr)
	if err != nil {
		return fmt.Errorf("listen: %w", err)
	}
	r.ln = ln

	fmt.Printf("[Receiver] Listening on %s\n", r.addr)

	r.wg.Add(1)
	go r.acceptLoop(handler)

	return nil
}

func (r *Receiver) acceptLoop(handler func(conn net.Conn)) {
	defer r.wg.Done()

	for {
		select {
		case <-r.done:
			return
		default:
		}

		r.ln.(*net.UnixListener).SetDeadline(time.Now().Add(1 * time.Second))

		conn, err := r.ln.Accept()
		if err != nil {
			if netErr, ok := err.(net.Error); ok && netErr.Timeout() {
				continue
			}
			if !isClosedError(err) {
				fmt.Printf("[Receiver] Accept error: %v\n", err)
			}
			return
		}

		r.wg.Add(1)
		go func() {
			defer r.wg.Done()
			handler(conn)
		}()
	}
}

func (r *Receiver) Stop() error {
	close(r.done)

	if r.ln != nil {
		return r.ln.Close()
	}
	return nil
}

func (r *Receiver) Wait() {
	r.wg.Wait()
}

func isClosedError(err error) bool {
	return err == net.ErrClosed
}
