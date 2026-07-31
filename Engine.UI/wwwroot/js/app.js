/*
 * Browser-side helpers for Engine.Chess.
 *
 * Move sounds are synthesised with the Web Audio API rather than loaded as files.
 * A handful of short tones cost nothing to download, never 404, and can be tuned
 * in code, which matters more here than fidelity: the sound only has to tell you
 * that a move landed and whether it was a capture.
 */
window.engineChess = (() => {
  let audio = null;

  /** Browsers refuse to start audio until the user has interacted with the page. */
  function context() {
    if (!audio) {
      const Ctor = window.AudioContext || window.webkitAudioContext;
      if (!Ctor) return null;
      audio = new Ctor();
    }
    if (audio.state === "suspended") audio.resume();
    return audio;
  }

  /** A single percussive tone: a quick attack and an exponential decay. */
  function tone(frequency, durationSeconds, type, gainPeak) {
    const ctx = context();
    if (!ctx) return;

    const now = ctx.currentTime;
    const oscillator = ctx.createOscillator();
    const gain = ctx.createGain();

    oscillator.type = type;
    oscillator.frequency.setValueAtTime(frequency, now);
    // A slight downward glide gives the click a wooden quality rather than a beep.
    oscillator.frequency.exponentialRampToValueAtTime(frequency * 0.7, now + durationSeconds);

    gain.gain.setValueAtTime(0.0001, now);
    gain.gain.exponentialRampToValueAtTime(gainPeak, now + 0.006);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + durationSeconds);

    oscillator.connect(gain).connect(ctx.destination);
    oscillator.start(now);
    oscillator.stop(now + durationSeconds + 0.02);
  }

  const sounds = {
    move: () => tone(320, 0.07, "triangle", 0.16),
    capture: () => tone(190, 0.11, "square", 0.13),
    castle: () => {
      tone(300, 0.06, "triangle", 0.14);
      setTimeout(() => tone(240, 0.07, "triangle", 0.12), 70);
    },
    check: () => {
      tone(660, 0.09, "triangle", 0.15);
      setTimeout(() => tone(880, 0.1, "triangle", 0.13), 80);
    },
    promote: () => {
      tone(520, 0.08, "triangle", 0.14);
      setTimeout(() => tone(780, 0.12, "triangle", 0.13), 80);
    },
    gameEnd: () => {
      tone(440, 0.13, "sine", 0.15);
      setTimeout(() => tone(330, 0.13, "sine", 0.14), 130);
      setTimeout(() => tone(220, 0.24, "sine", 0.13), 260);
    },
    illegal: () => tone(120, 0.09, "sawtooth", 0.08),
  };

  return {
    play(name) {
      const sound = sounds[name];
      if (sound) {
        try {
          sound();
        } catch {
          // Audio is a nicety; never let it break a move.
        }
      }
    },

    /**
     * Takes ownership of the board's pointer gestures.
     *
     * Dragging is handled here rather than in Blazor because a drag fires pointer
     * events at screen refresh rate, and asking the renderer to re-draw sixty-four
     * squares and thirty-two pieces that often is what makes a WebAssembly board
     * feel sticky. Instead the drag only writes two CSS custom properties on the
     * board element, which the browser resolves on the compositor. .NET is told
     * twice per gesture: once on press, once on drop.
     */
    attachBoard(board, owner) {
      if (!board) return;

      let drag = null;

      const cellAt = (rect, clientX, clientY) => {
        if (rect.width === 0 || rect.height === 0) return null;
        const column = Math.floor(((clientX - rect.left) / rect.width) * 8);
        const row = Math.floor(((clientY - rect.top) / rect.height) * 8);
        const outside = column < 0 || column > 7 || row < 0 || row > 7;
        return outside ? null : { row, column };
      };

      const clearOffset = () => {
        board.style.removeProperty("--drag-dx");
        board.style.removeProperty("--drag-dy");
      };

      const onPointerDown = (event) => {
        if (event.button !== 0) return;

        const rect = board.getBoundingClientRect();
        const cell = cellAt(rect, event.clientX, event.clientY);
        if (!cell) return;

        owner.invokeMethodAsync("HandlePress", cell.row, cell.column);

        // The classes come from the previous render, which is exactly the state
        // this press is acting on.
        const piece = document.elementFromPoint(event.clientX, event.clientY)?.closest?.(".piece");
        if (!piece || piece.classList.contains("not-yours")) return;

        event.preventDefault();
        drag = { rect, from: cell, moved: false };
        onPointerMove(event);
      };

      const onPointerMove = (event) => {
        if (!drag) return;

        const x = ((event.clientX - drag.rect.left) / drag.rect.width) * 100;
        const y = ((event.clientY - drag.rect.top) / drag.rect.height) * 100;

        // Offsets are expressed relative to the piece's own box, which is one
        // eighth of the board, hence the factor of eight.
        const dx = (x - 6.25 - drag.from.column * 12.5) * 8;
        const dy = (y - 6.25 - drag.from.row * 12.5) * 8;

        drag.moved = true;
        board.style.setProperty("--drag-dx", `${dx}%`);
        board.style.setProperty("--drag-dy", `${dy}%`);
      };

      const onPointerUp = (event) => {
        if (!drag) return;

        const finished = drag;
        drag = null;
        clearOffset();

        // A press and release without movement is a click, and the press has
        // already selected the piece.
        if (!finished.moved) {
          owner.invokeMethodAsync("HandleDragEnded");
          return;
        }

        const cell = cellAt(finished.rect, event.clientX, event.clientY);
        const sameSquare = cell && cell.row === finished.from.row && cell.column === finished.from.column;
        if (!cell || sameSquare) {
          owner.invokeMethodAsync("HandleDragEnded");
          return;
        }

        owner.invokeMethodAsync("HandleDrop", finished.from.row, finished.from.column, cell.row, cell.column);
      };

      board.addEventListener("pointerdown", onPointerDown);
      window.addEventListener("pointermove", onPointerMove, { passive: true });
      window.addEventListener("pointerup", onPointerUp);
      window.addEventListener("pointercancel", onPointerUp);

      board.__engineChessDetach = () => {
        board.removeEventListener("pointerdown", onPointerDown);
        window.removeEventListener("pointermove", onPointerMove);
        window.removeEventListener("pointerup", onPointerUp);
        window.removeEventListener("pointercancel", onPointerUp);
        clearOffset();
      };
    },

    detachBoard(board) {
      if (board && board.__engineChessDetach) {
        board.__engineChessDetach();
        delete board.__engineChessDetach;
      }
    },

    scrollToEnd(element) {
      if (element) element.scrollTop = element.scrollHeight;
    },
  };
})();
