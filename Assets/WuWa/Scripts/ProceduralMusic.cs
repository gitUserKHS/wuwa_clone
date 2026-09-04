using UnityEngine;

namespace WuWa
{
    /// Fully procedural BGM synthesis (guzheng-ish Karplus-Strong plucks, sine drums, band-limited saws).
    /// Two pieces: a calm 104 BPM exploration piece rendered as four vertical-layer stems, and a
    /// 160 BPM combat piece (driving kick pattern, gallop bass, synth-brass stabs, 16th-note lead runs,
    /// tom fills) rendered as four stems that loop seamlessly. Pure math - safe to run on a worker thread.
    public static class MusicGen
    {
        public const int SR = 44100;
        public const float ExploreBpm = 104f;
        public const float CombatBpm = 160f;
        public const int CombatBars = 16;
        /// One combat bar in seconds: the pickup fill is exactly one bar, the stems start on the next downbeat.
        public static float CombatBarSec { get { return 4f * 60f / CombatBpm; } }

        static float Freq(int semiFromA4) { return 440f * Mathf.Pow(2f, semiFromA4 / 12f); }

        // A minor pentatonic degrees relative to A: 0,3,5,7,10 (+ octaves)
        static readonly int[] Pent = { 0, 3, 5, 7, 10 };

        static void AddPad(float[] buf, float startSec, float durSec, float[] freqs, float amp)
        {
            int start = (int)(startSec * SR);
            int len = (int)(durSec * SR);
            float atk = durSec * 0.25f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float t = i / (float)SR;
                float env = t < atk ? t / atk : 1f - (t - atk) / (durSec - atk) * 0.65f;
                float s = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    s += Mathf.Sin(2f * Mathf.PI * freqs[f] * t) * 0.6f;
                    s += Mathf.Sin(2f * Mathf.PI * freqs[f] * 2.003f * t) * 0.12f;
                }
                buf[start + i] += s / freqs.Length * amp * env;
            }
        }

        /// Karplus-Strong pluck - sounds like a plucked zither string.
        static void AddPluck(float[] buf, float startSec, float freq, float durSec, float amp, System.Random rng)
        {
            int start = (int)(startSec * SR);
            int len = (int)(durSec * SR);
            int period = Mathf.Max(2, (int)(SR / freq));
            var delay = new float[period];
            for (int i = 0; i < period; i++) delay[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            int idx = 0;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float cur = delay[idx];
                int next = (idx + 1) % period;
                float filtered = (cur + delay[next]) * 0.5f * 0.9965f;
                delay[idx] = filtered;
                idx = next;
                float fade = 1f;
                int tail = len - i;
                if (tail < 2000) fade = tail / 2000f;
                buf[start + i] += cur * amp * fade;
            }
        }

        /// Band-limited sawtooth with a closing brightness sweep - synth-brass stabs and bass growl.
        static void AddSaw(float[] buf, float startSec, float freq, float durSec, float amp, float bright)
        {
            int start = (int)(startSec * SR);
            int len = (int)(durSec * SR);
            int harmonics = Mathf.Clamp((int)(9000f / freq), 1, 14);
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float t = i / (float)SR;
                float k = i / (float)len;
                float env = (t < 0.004f ? t / 0.004f : 1f) * Mathf.Pow(1f - k, 1.6f);
                float cut = bright * (1f - 0.7f * k);                  // the filter closes as the note decays
                float roll = (1f - cut) * 0.45f;
                float ph = 2f * Mathf.PI * freq * t;
                float s = 0f;
                for (int h = 1; h <= harmonics; h++)
                    s += Mathf.Sin(ph * h) / h * Mathf.Exp(-(h - 1) * roll);
                buf[start + i] += s * amp * env;
            }
        }

        static void AddKick(float[] buf, float startSec, float amp)
        {
            int start = (int)(startSec * SR);
            int len = (int)(0.16f * SR);
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float t = i / (float)SR;
                float env = Mathf.Pow(1f - t / 0.16f, 3f);
                float f = Mathf.Lerp(105f, 42f, t / 0.16f);
                buf[start + i] += Mathf.Sin(2f * Mathf.PI * f * t) * amp * env;
            }
        }

        /// Combat kick: fast 195-45 Hz pitch drop (phase-accumulated) plus a 6 ms click transient for punch.
        static void AddKickHard(float[] buf, float startSec, float amp)
        {
            int start = (int)(startSec * SR);
            const float dur = 0.21f;
            int len = (int)(dur * SR);
            float phase = 0f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float t = i / (float)SR;
                float k = t / dur;
                float f = 45f + 150f * Mathf.Pow(1f - k, 3.5f);
                phase += 2f * Mathf.PI * f / SR;
                float body = Mathf.Sin(phase) * Mathf.Pow(1f - k, 2.2f);
                float click = t < 0.006f ? Mathf.Sin(2f * Mathf.PI * 1600f * t) * (1f - t / 0.006f) * 0.5f : 0f;
                buf[start + i] += (body + click) * amp;
            }
        }

        /// Snare: bright noise burst over a 230-170 Hz tonal body.
        static void AddSnare(float[] buf, float startSec, float amp, System.Random rng)
        {
            int start = (int)(startSec * SR);
            int len = (int)(0.17f * SR);
            float last = 0f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float t = i / (float)SR;
                float k = i / (float)len;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = Mathf.Lerp(last, w, 0.75f);
                float noise = last * Mathf.Pow(1f - k, 2.0f);
                float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(230f, 170f, k) * t) * Mathf.Pow(1f - k, 4f);
                buf[start + i] += (noise * 0.75f + body * 0.6f) * amp;
            }
        }

        static void AddTom(float[] buf, float startSec, float freq, float amp)
        {
            int start = (int)(startSec * SR);
            int len = (int)(0.24f * SR);
            float phase = 0f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float k = i / (float)len;
                float f = freq * (1f + 0.35f * Mathf.Pow(1f - k, 4f));
                phase += 2f * Mathf.PI * f / SR;
                buf[start + i] += Mathf.Sin(phase) * Mathf.Pow(1f - k, 2.5f) * amp;
            }
        }

        static void AddNoiseHit(float[] buf, float startSec, float durSec, float amp, float lowpass, System.Random rng)
        {
            int start = (int)(startSec * SR);
            int len = (int)(durSec * SR);
            float last = 0f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float t = i / (float)len;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = Mathf.Lerp(last, w, lowpass);
                buf[start + i] += last * amp * Mathf.Pow(1f - t, 2.2f);
            }
        }

        static void AddCrash(float[] buf, float startSec, float amp, System.Random rng)
        {
            int start = (int)(startSec * SR);
            int len = (int)(1.2f * SR);
            float last = 0f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float k = i / (float)len;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = Mathf.Lerp(last, w, 0.8f);
                buf[start + i] += last * amp * Mathf.Pow(1f - k, 1.8f);
            }
        }

        /// Noise riser: the filter opens and the level swells toward the downbeat.
        static void AddRiser(float[] buf, float startSec, float durSec, float amp, System.Random rng)
        {
            int start = (int)(startSec * SR);
            int len = (int)(durSec * SR);
            float last = 0f;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                float k = i / (float)len;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = Mathf.Lerp(last, w, Mathf.Lerp(0.12f, 0.95f, k * k));
                buf[start + i] += last * amp * k * k;
            }
        }

        static void SoftClip(float[] d)
        {
            for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i] * 1.15f / (1f + Mathf.Abs(d[i]) * 0.6f), -1f, 1f);
        }

        static void LoopFade(float[] d)
        {
            int edge = SR / 6;
            for (int i = 0; i < edge; i++)
            {
                float k = i / (float)edge;
                d[i] *= k;
                d[d.Length - 1 - i] *= k;
            }
        }

        /// Fold everything rendered past the loop length back onto the start, so note decays ring across
        /// the seam and the loop has neither a gap nor a click. Requires buf.Length < 2n.
        static float[] FoldLoop(float[] buf, int n)
        {
            var o = new float[n];
            System.Array.Copy(buf, o, n);
            for (int i = n; i < buf.Length; i++) o[i - n] += buf[i];
            return o;
        }

        // chord roots as semitones from A4: Am=A3, F=F3, C=C4, G=G3, E=E3
        static readonly int[][] ChordTones =
        {
            new[] { -12, -9, -5 },   // Am: A3 C4 E4
            new[] { -16, -12, -9 },  // F:  F3 A3 C4
            new[] { -9, -5, -2 },    // C:  C4 E4 G4
            new[] { -14, -10, -7 },  // G:  G3 B3 D4
            new[] { -17, -13, -10 }, // E:  E3 G#3 B3 (harmonic-minor dominant, turnaround only)
        };

        /// Vertical layering (GDD ch.11): ONE piece at one tempo rendered as four
        /// synchronized stems - pad / melody / bass / percussion. All four play
        /// in lock-step forever; exploration mixes them by volume, so any
        /// transition timing is musically valid. Returns [pad, melody, bass, perc].
        public static float[][] Layers()
        {
            const float bpm = ExploreBpm;
            float beat = 60f / bpm;
            int bars = 16;
            float total = bars * 4f * beat;
            int n = (int)(total * SR) + SR;
            var pad = new float[n];
            var mel = new float[n];
            var bass = new float[n];
            var perc = new float[n];
            var rngM = new System.Random(1201);
            var rngB = new System.Random(88);
            var rngP = new System.Random(555);
            int[] prog = { 0, 0, 1, 3 };   // Am Am F G

            for (int bar = 0; bar < bars; bar++)
            {
                var chord = ChordTones[prog[bar % 4]];
                var freqs = new float[chord.Length];
                for (int i = 0; i < chord.Length; i++) freqs[i] = Freq(chord[i]);
                float bt0 = bar * 4f * beat;

                // --- pad: sustained chords + a slow low root
                AddPad(pad, bt0, 4f * beat * 1.04f, freqs, 0.16f);
                AddPluck(pad, bt0, Freq(chord[0] - 12), 2.4f, 0.10f, rngM);

                // --- melody: sparse zither arpeggios over the pentatonic
                for (int e = 0; e < 8; e++)
                {
                    if (rngM.NextDouble() > 0.34) continue;
                    int deg = Pent[rngM.Next(Pent.Length)];
                    int oct = rngM.NextDouble() < 0.55 ? 0 : 12;
                    AddPluck(mel, bt0 + e * beat * 0.5f, Freq(deg + oct), 1.2f, 0.17f, rngM);
                }
                if (bar % 4 == 3)                                     // phrase-end answer
                    AddPluck(mel, bt0 + 3.5f * beat, Freq(Pent[bar / 4 % Pent.Length] + 12), 1.4f, 0.15f, rngM);

                // --- bass: driving eighth roots
                for (int b = 0; b < 4; b++)
                {
                    float bt = bt0 + b * beat;
                    AddPluck(bass, bt, Freq(chord[0] - 24), beat * 0.9f, 0.24f, rngB);
                    AddPluck(bass, bt + beat * 0.5f, Freq(chord[0] - 24), beat * 0.45f, 0.14f, rngB);
                    if (b == 3 && rngB.NextDouble() < 0.5)
                        AddPluck(bass, bt + beat * 0.75f, Freq(chord[0] - 12), beat * 0.3f, 0.10f, rngB);
                }

                // --- percussion: kick four-on-floor, offbeat snare, eighth hats
                for (int b = 0; b < 4; b++)
                {
                    float bt = bt0 + b * beat;
                    AddKick(perc, bt, 0.5f);
                    if (b == 1 || b == 3) AddNoiseHit(perc, bt, 0.12f, 0.24f, 0.5f, rngP);
                    AddNoiseHit(perc, bt + beat * 0.5f, 0.03f, 0.08f, 0.85f, rngP);
                    if (bar % 4 == 3 && b == 3)
                    {
                        AddKick(perc, bt + beat * 0.5f, 0.45f);
                        AddNoiseHit(perc, bt + beat * 0.5f, 0.1f, 0.2f, 0.55f, rngP);
                    }
                }
            }

            SoftClip(pad); SoftClip(mel); SoftClip(bass); SoftClip(perc);
            LoopFade(pad); LoopFade(mel); LoopFade(bass); LoopFade(perc);
            return new[] { pad, mel, bass, perc };
        }

        /// Combat piece: 160 BPM, 16 bars (24.0 s), seamless loop, four synchronized stems
        /// [stabs+glue pad, lead, bass, drums]. Am Am F G x2 | F G Am Am | F F G E, with a
        /// tom fill every fourth bar and a snare-roll crescendo into the loop's downbeat crash.
        public static float[][] CombatLayers()
        {
            float beat = 60f / CombatBpm;
            float six = beat * 0.25f;
            int n = Mathf.RoundToInt(CombatBars * 4f * beat * SR);
            int nb = n + 3 * SR;
            var stab = new float[nb];
            var lead = new float[nb];
            var bass = new float[nb];
            var drum = new float[nb];
            var rngL = new System.Random(2026);
            var rngB = new System.Random(160);
            var rngD = new System.Random(909);
            int[] prog = { 0, 0, 1, 3, 0, 0, 1, 3, 1, 3, 0, 0, 1, 1, 3, 4 };
            int[][] motifs =
            {
                new[] { 0, 2, 3, 4 },   // A D E G  - climbing call
                new[] { 3, 2, 1, 0 },   // E D C A  - falling answer
                new[] { 0, 3, 4, 3 },   // A E G E  - leap
                new[] { 2, 3, 4, 2 },   // D E G D
            };
            int[] slots = { 0, 2, 3, 6, 8, 10, 11, 14 };          // syncopated 16th-note lead rhythm
            int[] stabEven = { 0, 6, 10 };
            int[] stabOdd = { 0, 6, 10, 14 };
            int[] eBarNotes = { -5, 2, 7, 2 };                    // E4 B4 E5 B4 over the E chord
            int[] eBarRun = { -5, -1, 2, 7, 11, 14, 11, 7 };      // E G# B E G# B B E arpeggio
            int[] mBuf = new int[4];

            for (int bar = 0; bar < CombatBars; bar++)
            {
                var chord = ChordTones[prog[bar]];
                int root = chord[0];
                bool eMajor = prog[bar] == 4;
                bool odd = (bar & 1) == 1;
                int inPhrase = bar % 4;
                float bt0 = bar * 4f * beat;

                // --- stabs: synth-brass power chords locked to the kick, plus a quiet glue pad
                var hits = odd ? stabOdd : stabEven;
                for (int h = 0; h < hits.Length; h++)
                {
                    float a = hits[h] == 0 ? 1f : 0.8f;
                    float ts = bt0 + hits[h] * six;
                    AddSaw(stab, ts, Freq(root), 0.22f, 0.16f * a, 0.85f);
                    AddSaw(stab, ts, Freq(root + 7), 0.22f, 0.13f * a, 0.85f);
                    AddSaw(stab, ts, Freq(root + 12), 0.22f, 0.10f * a, 0.85f);
                }
                AddPad(stab, bt0, 4f * beat * 1.02f, new[] { Freq(root - 12), Freq(root - 5) }, 0.05f);

                // --- bass: gallop (8th + two 16ths) on the low root, fifth kick-up at odd bar ends
                int low = root - 24;
                for (int b = 0; b < 4; b++)
                {
                    float bt = bt0 + b * beat;
                    AddPluck(bass, bt, Freq(low), beat * 0.55f, 0.30f, rngB);
                    AddSaw(bass, bt, Freq(low), beat * 0.5f, 0.07f, 0.55f);
                    AddPluck(bass, bt + 2f * six, Freq(low), six * 0.9f, 0.17f, rngB);
                    int last = (b == 3 && odd) ? low + 7 : low;
                    AddPluck(bass, bt + 3f * six, Freq(last), six * 0.9f, 0.17f, rngB);
                }

                // --- lead: one motif per phrase - call / call up a degree / reversed answer / run into the next phrase
                var motif = motifs[bar / 4];
                for (int j = 0; j < 4; j++)
                {
                    int idx = motif[j] + (inPhrase == 1 ? 1 : 0);
                    if (inPhrase == 2) idx = motif[3 - j];
                    mBuf[j] = Pent[idx % Pent.Length] + 12 * (idx / Pent.Length) + 12;
                }
                int hitsThisBar = inPhrase == 3 ? 4 : slots.Length;
                for (int s = 0; s < hitsThisBar; s++)
                {
                    int note = eMajor ? eBarNotes[s % 4] : mBuf[s % 4];
                    float ts = bt0 + slots[s] * six;
                    AddPluck(lead, ts, Freq(note), 0.42f, 0.30f, rngL);
                    if (slots[s] == 0 || slots[s] == 8) AddPluck(lead, ts, Freq(note - 12), 0.5f, 0.12f, rngL);
                }
                if (inPhrase == 3)
                {
                    for (int r = 0; r < 8; r++)                       // 16th-note run up the scale on beats 3-4
                    {
                        int note = eMajor ? eBarRun[r] : Pent[r % Pent.Length] + 12 * (r / Pent.Length) + 12;
                        AddPluck(lead, bt0 + (8 + r) * six, Freq(note), 0.3f, 0.28f, rngL);
                    }
                }
                if (inPhrase == 2)                                    // long held fifth over the answer bar
                    AddPluck(lead, bt0 + 8f * six, Freq(root + 7 + 12), 1.4f, 0.20f, rngL);

                // --- drums
                bool fillBar = inPhrase == 3;
                for (int s = 0; s < 16; s++)
                {
                    float ts = bt0 + s * six;
                    bool inFill = fillBar && s >= 8;
                    bool kick = inFill ? (s == 8 || s == 12)
                                       : (s == 0 || s == 6 || s == 8 || s == 10 || (odd && (s == 3 || s == 14)));
                    if (kick) AddKickHard(drum, ts, s == 0 ? 0.62f : 0.5f);
                    if (inFill) continue;
                    if (s == 4 || s == 12) AddSnare(drum, ts, 0.5f, rngD);
                    else if (odd && (s == 7 || s == 15)) AddSnare(drum, ts, 0.16f, rngD);   // ghost notes
                    if (odd && s == 14) AddNoiseHit(drum, ts, 0.14f, 0.13f, 0.9f, rngD);    // open hat
                    else AddNoiseHit(drum, ts, 0.028f, (s & 1) == 0 ? 0.11f : 0.06f, 0.95f, rngD);
                }
                if (fillBar)
                {
                    if (bar == CombatBars - 1)
                    {
                        for (int s = 8; s < 16; s++)                  // roll crescendo into the loop's crash
                            AddSnare(drum, bt0 + s * six, Mathf.Lerp(0.18f, 0.55f, (s - 8) / 7f), rngD);
                    }
                    else
                    {
                        AddSnare(drum, bt0 + 8f * six, 0.45f, rngD);
                        AddSnare(drum, bt0 + 9f * six, 0.35f, rngD);
                        AddTom(drum, bt0 + 10f * six, 200f, 0.45f);
                        AddTom(drum, bt0 + 11f * six, 200f, 0.4f);
                        AddTom(drum, bt0 + 12f * six, 130f, 0.5f);
                        AddTom(drum, bt0 + 13f * six, 130f, 0.45f);
                        AddSnare(drum, bt0 + 14f * six, 0.5f, rngD);
                        AddSnare(drum, bt0 + 15f * six, 0.3f, rngD);
                    }
                }
                if (inPhrase == 0) AddCrash(drum, bt0, (bar == 0 || bar == 8) ? 0.3f : 0.22f, rngD);
            }

            var outStab = FoldLoop(stab, n);
            var outLead = FoldLoop(lead, n);
            var outBass = FoldLoop(bass, n);
            var outDrum = FoldLoop(drum, n);
            SoftClip(outStab); SoftClip(outLead); SoftClip(outBass); SoftClip(outDrum);
            return new[] { outStab, outLead, outBass, outDrum };
        }

        /// One-bar pickup at the combat tempo (kick, toms, snare-roll crescendo, noise riser) that
        /// masks the explore-to-combat handoff; the combat stems start exactly one bar after it.
        public static float[] CombatPickup()
        {
            float beat = 60f / CombatBpm;
            float six = beat * 0.25f;
            var buf = new float[(int)((4f * beat + 0.4f) * SR)];
            var rng = new System.Random(31);
            AddKickHard(buf, 0f, 0.6f);
            AddSnare(buf, 2f * six, 0.3f, rng);
            AddKickHard(buf, 4f * six, 0.5f);
            AddTom(buf, 6f * six, 200f, 0.4f);
            AddTom(buf, 7f * six, 130f, 0.45f);
            AddKickHard(buf, 8f * six, 0.5f);
            AddKickHard(buf, 12f * six, 0.55f);
            for (int s = 8; s < 16; s++) AddSnare(buf, s * six, Mathf.Lerp(0.2f, 0.55f, (s - 8) / 7f), rng);
            AddRiser(buf, 0f, 4f * beat, 0.2f, rng);
            SoftClip(buf);
            return buf;
        }

        public static float[] VictorySting()
        {
            var buf = new float[(int)(3.4f * SR)];
            var rng = new System.Random(9);
            int[] arp = { -12, -9, -5, 0, 3, 12 };
            for (int i = 0; i < arp.Length; i++)
                AddPluck(buf, i * 0.14f, Freq(arp[i]), 1.8f, 0.22f, rng);
            AddPad(buf, 0.2f, 2.8f, new[] { Freq(-12), Freq(-9), Freq(-5), Freq(0) }, 0.14f);
            SoftClip(buf);
            return buf;
        }
    }
}
