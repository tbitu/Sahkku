/**
 * The open-license sound effects: a tiny procedural Web Audio synthesiser.
 *
 * Nothing is downloaded and nothing is bundled — every cue is built out of oscillators, envelopes and
 * short noise bursts at play time, so the client stays asset-free and 100 % permissive (MIT/CC0-equivalent:
 * you are hearing arithmetic, not a recording).
 *
 * Three properties matter, and each is deliberate:
 *
 * 1. **Total.** A browser without Web Audio, a context the user has not unlocked yet, or a synthesiser
 *    that throws for any reason must never break the game loop, so `play` folds all of it into a
 *    boolean and the caller ignores the result. Silence is always an acceptable outcome.
 * 2. **Autoplay-policy safe.** `play` never awaits: it nudges a *suspended* context with `resume()`
 *    and schedules the cue anyway, so the first sound after a cold page load either plays or is dropped
 *    by the browser — it never throws an `NotAllowedError` into the UI. `unlock()` is the explicit,
 *    awaited version a "first user gesture" handler should call.
 * 3. **Testable without a browser.** The AudioContext is reached through {@link AudioContextLike}, a
 *    structural seam a test can fake, and through a factory that is only consulted lazily, so importing
 *    this module in Node touches no browser global.
 *
 * The signal chain is voices → `sfxGain` → `masterGain` → destination, which is what makes the two
 * volume sliders (and the mute toggle) independent of the recipe of any single cue.
 */

/** Every cue the client can play. Kept as a value so a host can enumerate them. */
export const SoundNames = ["roll", "select", "move", "capture", "recruit", "victory"] as const;

export type SoundName = (typeof SoundNames)[number];

/**
 * The narrowest seam a caller needs to be audible: the controller takes one of these and defaults to
 * silence, which is what keeps it DOM-free and testable with a recording stub.
 */
export interface SoundPlayer {
  /** Plays one cue. Returns whether anything was actually scheduled. Must never throw. */
  play(name: SoundName): boolean;
}

// ----------------------------------------------------------------------------------------------
// The Web Audio seam
// ----------------------------------------------------------------------------------------------

/**
 * The parts of `AudioParam` the synthesiser automates. Method results are `unknown` so the real DOM
 * types (which return `AudioParam`) and a minimal fake (which returns `this`) are both assignable.
 */
export interface AudioParamLike {
  value: number;
  setValueAtTime(value: number, startTime: number): unknown;
  linearRampToValueAtTime(value: number, endTime: number): unknown;
  exponentialRampToValueAtTime(value: number, endTime: number): unknown;
  cancelScheduledValues?(startTime: number): unknown;
}

export interface AudioNodeLike {
  connect(destination: unknown): unknown;
  disconnect(): void;
}

export interface GainNodeLike extends AudioNodeLike {
  gain: AudioParamLike;
}

export interface OscillatorNodeLike extends AudioNodeLike {
  type: string;
  frequency: AudioParamLike;
  start(when?: number): void;
  stop(when?: number): void;
}

export interface AudioBufferLike {
  readonly length: number;
  readonly sampleRate: number;
  getChannelData(channel: number): Float32Array;
}

export interface AudioBufferSourceNodeLike extends AudioNodeLike {
  buffer: AudioBufferLike | null;
  loop: boolean;
  start(when?: number, offset?: number, duration?: number): void;
  stop(when?: number): void;
}

export interface BiquadFilterNodeLike extends AudioNodeLike {
  type: string;
  frequency: AudioParamLike;
  Q: AudioParamLike;
}

/**
 * The slice of `AudioContext` the client uses. `createBiquadFilter` is optional because a host that
 * cannot filter still deserves a (slightly duller) cue rather than none.
 */
export interface AudioContextLike {
  readonly currentTime: number;
  readonly state: string;
  /** The context's sample rate; optional so a fake may omit it (the manager then assumes 44.1 kHz). */
  readonly sampleRate?: number;
  readonly destination: AudioNodeLike;
  resume(): Promise<void>;
  close?(): Promise<void>;
  createGain(): GainNodeLike;
  createOscillator(): OscillatorNodeLike;
  createBufferSource(): AudioBufferSourceNodeLike;
  createBuffer(channels: number, length: number, sampleRate: number): AudioBufferLike;
  createBiquadFilter?(): BiquadFilterNodeLike;
}

/** Builds the platform's audio context, or `null` where there is none (Node, or an old browser). */
export type AudioContextFactory = () => AudioContextLike | null;

export interface AudioManagerInit {
  /** Defaults to the platform context, i.e. `window.AudioContext`. */
  contextFactory?: AudioContextFactory | null;

  /** Master volume, `0..1`. Defaults to `1`. */
  masterVolume?: number;

  /** Sound-effects volume, `0..1`. Defaults to `0.7`. */
  sfxVolume?: number;

  /** Start muted. Defaults to `false`. */
  muted?: boolean;

  /** Noise source for the rattles and thuds; injectable so a test can pin the waveform. */
  random?: () => number;
}

/**
 * The synthesised sound effects and their two volume stages.
 *
 * Construct one per client. Nothing happens until a cue is played, so a manager created on page load
 * costs nothing — and on a page where Web Audio is missing, `available` is false and every call is a
 * quiet no-op.
 */
export class AudioManager implements SoundPlayer {
  private readonly contextFactory: AudioContextFactory;
  private readonly random: () => number;

  private audioContext: AudioContextLike | null = null;
  private masterGain: GainNodeLike | null = null;
  private sfxGain: GainNodeLike | null = null;
  private noiseBuffer: AudioBufferLike | null = null;

  private master = 1;
  private sfx = 0.7;
  private isMuted = false;

  constructor(init: AudioManagerInit = {}) {
    this.contextFactory = init.contextFactory ?? defaultAudioContextFactory;
    this.random = init.random ?? Math.random;
    this.master = clamp01(init.masterVolume ?? 1);
    this.sfx = clamp01(init.sfxVolume ?? 0.7);
    this.isMuted = init.muted ?? false;
  }

  // ------------------------------------------------------------------ volume and mute

  /** True once a real context could be built; a manager without one still accepts every call. */
  get available(): boolean {
    return this.ensureContext() != null;
  }

  get muted(): boolean {
    return this.isMuted;
  }

  /** Master volume in `0..1`. */
  get masterVolume(): number {
    return this.master;
  }

  /** Sound-effects volume in `0..1`. */
  get sfxVolume(): number {
    return this.sfx;
  }

  /** The effective gain a cue would be heard at: mute and both sliders folded together. */
  get effectiveVolume(): number {
    return this.isMuted ? 0 : this.master * this.sfx;
  }

  /**
   * The state of the context that already exists (`"running"`, `"suspended"`, …), or `null` when none
   * has been needed yet. Reading it never builds one — that is what `available` and `unlock` are for.
   */
  get contextState(): string | null {
    return this.audioContext?.state ?? null;
  }

  setMuted(muted: boolean): void {
    this.isMuted = muted === true;
    this.applyGains();
  }

  /** Sets master and/or sound-effects volume, each clamped into `0..1`. */
  setVolumes(volumes: { master?: number; sfx?: number }): void {
    if (volumes.master != null) this.master = clamp01(volumes.master);
    if (volumes.sfx != null) this.sfx = clamp01(volumes.sfx);
    this.applyGains();
  }

  /**
   * Resumes a context the autoplay policy left suspended. Call it from the first real user gesture
   * (a click or a key press) — that gesture is what the browser is waiting for. Resolves `false` when
   * there is no context or the browser refused, and never rejects.
   */
  async unlock(): Promise<boolean> {
    const context = this.ensureContext();
    if (context == null) return false;

    try {
      if (context.state === "running") return true;
      await context.resume();
      return context.state === "running" || context.state === "suspended";
    } catch {
      return false;
    }
  }

  /** Releases the context if the platform allows it. Safe to call twice. */
  dispose(): void {
    const context = this.audioContext;
    this.audioContext = null;
    this.masterGain = null;
    this.sfxGain = null;
    this.noiseBuffer = null;
    if (context == null) return;
    try {
      void context.close?.();
    } catch {
      // A context that refuses to close is the platform's business, not the game's.
    }
  }

  // ------------------------------------------------------------------ the cues

  /** Plays one cue. Total: returns `false` (silence) instead of throwing on any failure. */
  play(name: SoundName): boolean {
    if (this.isMuted) return false;

    const context = this.ensureContext();
    if (context == null || this.sfxGain == null) return false;
    if (this.effectiveVolume <= 0) return false;

    try {
      this.resumeWithoutBlocking(context);
      const now = context.currentTime + AudioManager.StartOffsetSeconds;

      switch (name) {
        case "roll":
          this.synthesizeRoll(context, now);
          break;
        case "select":
          this.synthesizeSelect(context, now);
          break;
        case "move":
          this.synthesizeMove(context, now);
          break;
        case "capture":
          this.synthesizeCapture(context, now);
          break;
        case "recruit":
          this.synthesizeRecruit(context, now);
          break;
        case "victory":
          this.synthesizeVictory(context, now);
          break;
        default:
          return false;
      }
      return true;
    } catch {
      return false;
    }
  }

  /** The dice rattle of a throw. */
  playRoll(): boolean {
    return this.play("roll");
  }

  /** The gentle high click of picking a piece up. */
  playSelect(): boolean {
    return this.play("select");
  }

  /** The wooden slide-and-thud of a piece landing. */
  playMove(): boolean {
    return this.play("move");
  }

  /** The snap of a capture. */
  playCapture(): boolean {
    return this.play("capture");
  }

  /** The regal chime of a king being recruited. */
  playRecruit(): boolean {
    return this.play("recruit");
  }

  /** The ascending fanfare of a won match. */
  playVictory(): boolean {
    return this.play("victory");
  }

  // ------------------------------------------------------------------ recipes

  /** Dice rattle: a handful of short filtered noise bursts falling over half a second. */
  private synthesizeRoll(context: AudioContextLike, now: number): void {
    const bursts = 6;
    for (let index = 0; index < bursts; ++index) {
      const spread = index / bursts;
      const when = now + spread * 0.42 + this.random() * 0.03;
      this.noise(context, when, {
        duration: 0.05 - spread * 0.02,
        peak: 0.22 - spread * 0.06,
        filterHz: 2600,
        filterQ: 0.8,
      });
    }
    // A short wooden click under the rattle, so the throw has a body rather than only a hiss.
    this.tone(context, now + 0.02, {
      type: "triangle",
      from: 320,
      to: 150,
      duration: 0.12,
      peak: 0.12,
    });
  }

  /** Piece selection: one bright, very short blip. */
  private synthesizeSelect(context: AudioContextLike, now: number): void {
    this.tone(context, now, {
      type: "sine",
      from: 1046.5,
      to: 1568,
      duration: 0.09,
      peak: 0.13,
      attack: 0.004,
    });
  }

  /** Piece move: a wooden slide (filtered noise) ending in a low thud. */
  private synthesizeMove(context: AudioContextLike, now: number): void {
    this.noise(context, now, { duration: 0.07, peak: 0.14, filterHz: 1200, filterQ: 0.7 });
    this.tone(context, now + 0.02, {
      type: "triangle",
      from: 180,
      to: 82,
      duration: 0.19,
      peak: 0.24,
      attack: 0.006,
    });
  }

  /** Capture: a sharp noise smack with a descending square snap under it. */
  private synthesizeCapture(context: AudioContextLike, now: number): void {
    this.noise(context, now, { duration: 0.1, peak: 0.3, filterHz: 2200, filterQ: 1.4 });
    this.tone(context, now, {
      type: "square",
      from: 330,
      to: 160,
      duration: 0.14,
      peak: 0.17,
      attack: 0.003,
    });
  }

  /** King recruitment: a bell-like chord that rings on. */
  private synthesizeRecruit(context: AudioContextLike, now: number): void {
    const partials: Array<{ frequency: number; peak: number; duration: number }> = [
      { frequency: 523.25, peak: 0.16, duration: 1.2 },
      { frequency: 783.99, peak: 0.11, duration: 1.0 },
      { frequency: 1046.5, peak: 0.07, duration: 0.8 },
    ];
    for (const partial of partials) {
      this.tone(context, now, {
        type: "sine",
        from: partial.frequency,
        to: partial.frequency,
        duration: partial.duration,
        peak: partial.peak,
        attack: 0.012,
      });
    }
    this.tone(context, now, {
      type: "triangle",
      from: 261.63,
      to: 261.63,
      duration: 0.9,
      peak: 0.09,
      attack: 0.02,
    });
  }

  /** Victory: an ascending five-note fanfare that ends on a held chord. */
  private synthesizeVictory(context: AudioContextLike, now: number): void {
    const melody = [523.25, 659.25, 783.99, 1046.5, 1318.51];
    melody.forEach((frequency, index) => {
      const when = now + index * 0.14;
      this.tone(context, when, {
        type: "triangle",
        from: frequency,
        to: frequency,
        duration: 0.34,
        peak: 0.16,
        attack: 0.01,
      });
    });

    const chordAt = now + melody.length * 0.14;
    for (const frequency of [523.25, 659.25, 783.99]) {
      this.tone(context, chordAt, {
        type: "sine",
        from: frequency,
        to: frequency,
        duration: 0.9,
        peak: 0.12,
        attack: 0.02,
      });
    }
  }

  // ------------------------------------------------------------------ voices

  /** One enveloped oscillator: attack into an exponential decay, optionally gliding in pitch. */
  private tone(
    context: AudioContextLike,
    when: number,
    spec: {
      type: string;
      from: number;
      to: number;
      duration: number;
      peak: number;
      attack?: number;
    },
  ): void {
    const gain = this.sfxGain;
    if (gain == null) return;

    const attack = spec.attack ?? 0.008;
    const oscillator = context.createOscillator();
    oscillator.type = spec.type;
    oscillator.frequency.setValueAtTime(spec.from, when);
    if (spec.to !== spec.from) {
      oscillator.frequency.exponentialRampToValueAtTime(Math.max(1, spec.to), when + spec.duration);
    }

    const envelope = context.createGain();
    envelope.gain.setValueAtTime(AudioManager.Silence, when);
    envelope.gain.exponentialRampToValueAtTime(
      Math.max(AudioManager.Silence, spec.peak),
      when + attack,
    );
    envelope.gain.exponentialRampToValueAtTime(AudioManager.Silence, when + spec.duration);

    oscillator.connect(envelope);
    envelope.connect(gain);
    oscillator.start(when);
    oscillator.stop(when + spec.duration + 0.02);
  }

  /** One noise burst, optionally band-limited by a low-pass. */
  private noise(
    context: AudioContextLike,
    when: number,
    spec: { duration: number; peak: number; filterHz: number; filterQ: number },
  ): void {
    const gain = this.sfxGain;
    if (gain == null) return;

    const buffer = this.ensureNoiseBuffer(context);
    if (buffer == null) return;

    const source = context.createBufferSource();
    source.buffer = buffer;
    source.loop = false;

    const envelope = context.createGain();
    envelope.gain.setValueAtTime(AudioManager.Silence, when);
    envelope.gain.exponentialRampToValueAtTime(
      Math.max(AudioManager.Silence, spec.peak),
      when + 0.005,
    );
    envelope.gain.exponentialRampToValueAtTime(AudioManager.Silence, when + spec.duration);

    const filter = context.createBiquadFilter?.() ?? null;
    if (filter == null) {
      source.connect(envelope);
    } else {
      filter.type = "lowpass";
      filter.frequency.setValueAtTime(spec.filterHz, when);
      filter.Q.setValueAtTime(spec.filterQ, when);
      source.connect(filter);
      filter.connect(envelope);
    }
    envelope.connect(gain);

    const offset = buffer.length > 0 ? this.random() * (buffer.length / buffer.sampleRate) : 0;
    source.start(when, offset, spec.duration + 0.02);
    source.stop(when + spec.duration + 0.04);
  }

  // ------------------------------------------------------------------ context plumbing

  /**
   * Builds the context and its two gain stages on first use. Returns `null` when the platform has no
   * Web Audio or the constructor refused; the failure is remembered only implicitly (the factory is
   * cheap), so a browser that gains a context after a user gesture still works.
   */
  private ensureContext(): AudioContextLike | null {
    if (this.audioContext != null) return this.audioContext;

    let context: AudioContextLike | null = null;
    try {
      context = this.contextFactory();
    } catch {
      context = null;
    }
    if (context == null) return null;

    try {
      const masterGain = context.createGain();
      const sfxGain = context.createGain();
      sfxGain.connect(masterGain);
      masterGain.connect(context.destination);

      this.audioContext = context;
      this.masterGain = masterGain;
      this.sfxGain = sfxGain;
      this.applyGains();
      return context;
    } catch {
      this.audioContext = null;
      this.masterGain = null;
      this.sfxGain = null;
      return null;
    }
  }

  /** Pushes the current master/sfx/mute state into the gain stages. */
  private applyGains(): void {
    const master = this.masterGain;
    const sfx = this.sfxGain;
    try {
      if (master != null) master.gain.value = this.isMuted ? 0 : this.master;
      if (sfx != null) sfx.gain.value = this.sfx;
    } catch {
      // A host whose gains reject assignment is already silent; nothing to do.
    }
  }

  /**
   * Nudges a suspended context awake *without* awaiting it: the current gesture is what lets the
   * browser resume, and a rejection here must not surface as an unhandled promise.
   */
  private resumeWithoutBlocking(context: AudioContextLike): void {
    if (context.state === "running") return;
    try {
      void Promise.resolve(context.resume()).catch(() => undefined);
    } catch {
      // Synchronously refusing to resume is the same outcome as a rejected promise.
    }
  }

  /** White noise, generated once per context and reused by every burst. */
  private ensureNoiseBuffer(context: AudioContextLike): AudioBufferLike | null {
    if (this.noiseBuffer != null) return this.noiseBuffer;

    try {
      const sampleRate = Math.max(8000, Math.floor(context.sampleRate ?? 44100));
      const buffer = context.createBuffer(1, Math.floor(sampleRate * 0.5), sampleRate);
      const channel = buffer.getChannelData(0);
      for (let index = 0; index < channel.length; ++index) {
        channel[index] = this.random() * 2 - 1;
      }
      this.noiseBuffer = buffer;
      return buffer;
    } catch {
      return null;
    }
  }

  /** An exponential ramp cannot reach (or start from) zero, so silence is this small value. */
  private static readonly Silence = 0.0001;

  /** Cues are scheduled a hair in the future so the attack is not clipped by "now". */
  private static readonly StartOffsetSeconds = 0.01;
}

/** The platform context, looked up lazily and defensively. `null` outside a browser. */
function defaultAudioContextFactory(): AudioContextLike | null {
  const scope = globalThis as {
    AudioContext?: new () => unknown;
    webkitAudioContext?: new () => unknown;
  };
  const ctor = scope.AudioContext ?? scope.webkitAudioContext;
  if (typeof ctor !== "function") return null;

  try {
    return new ctor() as AudioContextLike;
  } catch {
    return null;
  }
}

function clamp01(value: number): number {
  if (!Number.isFinite(value)) return 0;
  if (value < 0) return 0;
  if (value > 1) return 1;
  return value;
}
