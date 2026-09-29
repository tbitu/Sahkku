/**
 * Audio manager tests — `T4_AUDIO_LIFECYCLE`.
 *
 * The synthesizer is exercised against a fake `AudioContext` (the module's own structural seam), which
 * is what lets the suite prove the two properties that matter for a game that must never be interrupted
 * by sound: **every cue schedules something** and **no failure escapes**. The fake records every node
 * and automation the manager creates, so "it played" is asserted rather than assumed.
 *
 * The failure paths are covered on purpose: a browser without Web Audio, a context the autoplay policy
 * keeps suspended, a constructor that throws, and a node factory that throws. All four have to end in
 * silence, not in an exception.
 */

import { describe, expect, it } from "vitest";

import {
  AudioManager,
  SoundNames,
  type AudioBufferLike,
  type AudioBufferSourceNodeLike,
  type AudioContextLike,
  type AudioNodeLike,
  type AudioParamLike,
  type BiquadFilterNodeLike,
  type GainNodeLike,
  type OscillatorNodeLike,
} from "../../src/audio/audio";

// ---------------------------------------------------------------------------------- fakes

/** One automation call, so a test can assert the envelope was actually scheduled. */
interface Automation {
  kind: string;
  value: number;
  time: number;
}

class FakeParam implements AudioParamLike {
  value = 0;
  readonly automation: Automation[] = [];

  setValueAtTime(value: number, startTime: number): unknown {
    this.value = value;
    this.automation.push({ kind: "set", value, time: startTime });
    return this;
  }

  linearRampToValueAtTime(value: number, endTime: number): unknown {
    this.value = value;
    this.automation.push({ kind: "linear", value, time: endTime });
    return this;
  }

  exponentialRampToValueAtTime(value: number, endTime: number): unknown {
    this.value = value;
    this.automation.push({ kind: "exponential", value, time: endTime });
    return this;
  }

  cancelScheduledValues(startTime: number): unknown {
    this.automation.push({ kind: "cancel", value: 0, time: startTime });
    return this;
  }
}

class FakeNode implements AudioNodeLike {
  readonly connections: unknown[] = [];

  connect(destination: unknown): unknown {
    this.connections.push(destination);
    return destination;
  }

  disconnect(): void {
    this.connections.length = 0;
  }
}

class FakeGain extends FakeNode implements GainNodeLike {
  readonly gain = new FakeParam();
}

class FakeOscillator extends FakeNode implements OscillatorNodeLike {
  type = "sine";
  readonly frequency = new FakeParam();
  startedAt: number | null = null;
  stoppedAt: number | null = null;

  start(when?: number): void {
    this.startedAt = when ?? 0;
  }

  stop(when?: number): void {
    this.stoppedAt = when ?? 0;
  }
}

class FakeBuffer implements AudioBufferLike {
  readonly data: Float32Array;

  constructor(
    readonly length: number,
    readonly sampleRate: number,
  ) {
    this.data = new Float32Array(length);
  }

  getChannelData(_channel: number): Float32Array {
    return this.data;
  }
}

class FakeBufferSource extends FakeNode implements AudioBufferSourceNodeLike {
  buffer: AudioBufferLike | null = null;
  loop = false;
  startedAt: number | null = null;
  stoppedAt: number | null = null;

  start(when?: number, _offset?: number, _duration?: number): void {
    this.startedAt = when ?? 0;
  }

  stop(when?: number): void {
    this.stoppedAt = when ?? 0;
  }
}

class FakeFilter extends FakeNode implements BiquadFilterNodeLike {
  type = "lowpass";
  readonly frequency = new FakeParam();
  readonly Q = new FakeParam();
}

class FakeAudioContext implements AudioContextLike {
  currentTime = 0;
  state = "suspended";
  sampleRate = 8000;
  readonly destination = new FakeNode();

  readonly gains: FakeGain[] = [];
  readonly oscillators: FakeOscillator[] = [];
  readonly sources: FakeBufferSource[] = [];
  readonly filters: FakeFilter[] = [];
  readonly buffers: FakeBuffer[] = [];

  resumeCalls = 0;
  closeCalls = 0;
  /** When set, the node factory throws instead of returning a node. */
  failOnCreate = false;

  resume(): Promise<void> {
    this.resumeCalls++;
    this.state = "running";
    return Promise.resolve();
  }

  close(): Promise<void> {
    this.closeCalls++;
    return Promise.resolve();
  }

  createGain(): GainNodeLike {
    if (this.failOnCreate) throw new Error("no gains today");
    const gain = new FakeGain();
    this.gains.push(gain);
    return gain;
  }

  createOscillator(): OscillatorNodeLike {
    if (this.failOnCreate) throw new Error("no oscillators today");
    const oscillator = new FakeOscillator();
    this.oscillators.push(oscillator);
    return oscillator;
  }

  createBufferSource(): AudioBufferSourceNodeLike {
    if (this.failOnCreate) throw new Error("no sources today");
    const source = new FakeBufferSource();
    this.sources.push(source);
    return source;
  }

  createBuffer(_channels: number, length: number, sampleRate: number): AudioBufferLike {
    if (this.failOnCreate) throw new Error("no buffers today");
    const buffer = new FakeBuffer(length, sampleRate);
    this.buffers.push(buffer);
    return buffer;
  }

  createBiquadFilter(): BiquadFilterNodeLike {
    if (this.failOnCreate) throw new Error("no filters today");
    const filter = new FakeFilter();
    this.filters.push(filter);
    return filter;
  }
}

/** A manager over a fresh fake context, built on demand so a test can inspect both. */
function managerOn(
  context: FakeAudioContext,
  init: Parameters<typeof AudioManager.prototype.setVolumes>[0] = {},
): AudioManager {
  const manager = new AudioManager({
    contextFactory: () => context,
    random: () => 0.5,
  });
  manager.setVolumes(init);
  return manager;
}

// ---------------------------------------------------------------------------------- tests

describe("AudioManager: lifecycle (T4_AUDIO_LIFECYCLE)", () => {
  it("schedules an audible envelope for every cue and never throws", () => {
    for (const name of SoundNames) {
      const context = new FakeAudioContext();
      const manager = managerOn(context);

      expect(manager.play(name), `${name} should be scheduled`).toBe(true);
      expect(
        context.oscillators.length + context.sources.length,
        `${name} scheduled nothing`,
      ).toBeGreaterThan(0);

      // Every voice is enveloped: at least one gain node ramps down towards silence.
      const enveloped = context.gains.some((gain) =>
        gain.gain.automation.some((entry) => entry.kind === "exponential" && entry.value < 0.001),
      );
      expect(enveloped, `${name} has no decay`).toBe(true);
    }
  });

  it("gives each cue its own character", () => {
    const roll = new FakeAudioContext();
    managerOn(roll).playRoll();
    // A rattle is a burst of noise, not a single tone.
    expect(roll.sources.length).toBeGreaterThan(1);
    expect(roll.buffers).toHaveLength(1);

    const victory = new FakeAudioContext();
    managerOn(victory).playVictory();
    // The fanfare is a melody plus a closing chord.
    expect(victory.oscillators.length).toBeGreaterThanOrEqual(6);
    expect(victory.sources).toHaveLength(0);

    const select = new FakeAudioContext();
    managerOn(select).playSelect();
    expect(select.oscillators).toHaveLength(1);
    expect(select.oscillators[0]!.type).toBe("sine");
    expect(select.oscillators[0]!.startedAt).not.toBeNull();
    expect(select.oscillators[0]!.stoppedAt).not.toBeNull();
  });

  it("reuses one noise buffer and one gain chain per context", () => {
    const context = new FakeAudioContext();
    const manager = managerOn(context);

    manager.playRoll();
    manager.play("capture");
    manager.play("move");

    expect(context.buffers).toHaveLength(1);
    // Two gain stages: the sound-effects bus and the master bus.
    expect(context.gains.slice(0, 2).map((gain) => gain.connections.length)).toEqual([1, 1]);
  });

  it("mutes silently, and setting a volume to zero is the same as muting", () => {
    const context = new FakeAudioContext();
    const manager = managerOn(context, { master: 1, sfx: 0.5 });

    expect(manager.play("move")).toBe(true);
    const before = context.oscillators.length;

    manager.setMuted(true);
    expect(manager.play("move")).toBe(false);
    expect(manager.muted).toBe(true);
    expect(manager.effectiveVolume).toBe(0);
    expect(context.oscillators.length).toBe(before);

    manager.setMuted(false);
    manager.setVolumes({ sfx: 0 });
    expect(manager.play("move")).toBe(false);
    expect(context.oscillators.length).toBe(before);

    manager.setVolumes({ sfx: 1 });
    expect(manager.play("move")).toBe(true);
  });

  it("clamps volumes and pushes them into the two gain stages", () => {
    const context = new FakeAudioContext();
    const manager = managerOn(context, { master: 0.8, sfx: 0.25 });

    expect(manager.masterVolume).toBeCloseTo(0.8);
    expect(manager.sfxVolume).toBeCloseTo(0.25);
    expect(manager.effectiveVolume).toBeCloseTo(0.2);

    // Nothing is built until the client asks for audio: `available` is that probe.
    expect(context.gains).toHaveLength(0);
    expect(manager.available).toBe(true);

    // The master bus is created first, the sound-effects bus onto it; each carries its own slider.
    expect(context.gains[0]!.gain.value).toBeCloseTo(0.8);
    expect(context.gains[1]!.gain.value).toBeCloseTo(0.25);

    manager.setVolumes({ master: 2, sfx: -1 });
    expect(manager.masterVolume).toBe(1);
    expect(manager.sfxVolume).toBe(0);
    expect(context.gains[0]!.gain.value).toBeCloseTo(1);

    manager.setVolumes({ master: Number.NaN });
    expect(manager.masterVolume).toBe(0);
  });

  it("resumes a suspended context without blocking, and reports the state", () => {
    const context = new FakeAudioContext();
    context.state = "suspended";
    const manager = managerOn(context);

    // No context has been needed yet, so there is no state to report...
    expect(manager.contextState).toBeNull();
    expect(manager.available).toBe(true);
    expect(manager.contextState).toBe("suspended");

    // Playing while suspended schedules the cue anyway and nudges the context awake.
    expect(manager.play("select")).toBe(true);
    expect(context.resumeCalls).toBe(1);
  });

  it("unlocks on demand and reports whether it worked", async () => {
    const context = new FakeAudioContext();
    const manager = managerOn(context);

    await expect(manager.unlock()).resolves.toBe(true);
    expect(context.resumeCalls).toBe(1);
    expect(context.state).toBe("running");

    // A second unlock on a running context does not resume again.
    await expect(manager.unlock()).resolves.toBe(true);
    expect(context.resumeCalls).toBe(1);
  });

  it("is silent, and never throws, without Web Audio at all", async () => {
    const manager = new AudioManager({ contextFactory: () => null });

    expect(manager.available).toBe(false);
    expect(manager.contextState).toBeNull();
    for (const name of SoundNames) expect(manager.play(name)).toBe(false);
    await expect(manager.unlock()).resolves.toBe(false);
    expect(manager.playRoll()).toBe(false);
    expect(manager.playSelect()).toBe(false);
    expect(manager.playMove()).toBe(false);
    expect(manager.playCapture()).toBe(false);
    expect(manager.playRecruit()).toBe(false);
    expect(manager.playVictory()).toBe(false);
    manager.dispose();
  });

  it("survives a context constructor or a node factory that throws", () => {
    const refusing = new AudioManager({
      contextFactory: () => {
        throw new Error("the platform said no");
      },
    });
    expect(refusing.play("roll")).toBe(false);
    expect(refusing.available).toBe(false);

    const broken = new FakeAudioContext();
    broken.failOnCreate = true;
    const manager = new AudioManager({ contextFactory: () => broken });
    expect(manager.available).toBe(false);
    expect(manager.play("roll")).toBe(false);
  });

  it("closes the context on dispose and starts fresh afterwards", () => {
    const context = new FakeAudioContext();
    const manager = managerOn(context);

    expect(manager.play("select")).toBe(true);
    manager.dispose();
    expect(context.closeCalls).toBe(1);
    expect(manager.contextState).toBeNull();

    // A new context is built on the next cue, so a disposed manager still works.
    expect(manager.play("select")).toBe(true);
    expect(context.oscillators.length).toBe(2);
  });

  it("reflects the constructor's initial volumes and mute", () => {
    const context = new FakeAudioContext();
    const manager = new AudioManager({
      contextFactory: () => context,
      masterVolume: 0.5,
      sfxVolume: 0.4,
      muted: true,
    });

    expect(manager.muted).toBe(true);
    expect(manager.effectiveVolume).toBe(0);

    // A muted cue does no audio work at all: no context is even built for it.
    expect(manager.play("victory")).toBe(false);
    expect(context.gains).toHaveLength(0);

    // Once the chain exists it carries the constructor's volumes, with the muted master at zero.
    expect(manager.available).toBe(true);
    expect(context.gains[0]!.gain.value).toBe(0);
    expect(context.gains[1]!.gain.value).toBeCloseTo(0.4);
  });
});
