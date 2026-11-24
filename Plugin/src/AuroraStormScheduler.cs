// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEngine;

[ExecuteAlways]
public class AuroraStormScheduler : MonoBehaviour
{
    const float MaxAnimationWarpRate = 100f;

    public Material auroraMaterial;

    public float worldScale = 1f;

    [Range(0f, 1f)] public float currentStormIntensity = 0f;
    public bool manualOverride = false;

    public float baseMatActivity   = 0.30f;
    public float baseMatAnimSpeed  = 0.35f;
    public float baseMatAltTop     = 2.00f;

    public float stormMatActivity  = 1.00f;
    public float stormMatAnimSpeed = 0.75f;
    public float stormMatAltTop    = 2.30f;

    public bool  driveOval        = false;
    public float baseLatCenter    = 0.65f, stormLatCenter    = 0.65f;
    public float baseBandSpacing  = 1.0f,  stormBandSpacing  = 1.0f;
    public float baseLatWidth     = 0.03f, stormLatWidth     = 0.03f;
    public float baseFoldStrength = 0.12f, stormFoldStrength = 0.12f;
    public float arc2Threshold    = 0f,    arc3Threshold     = 0f;

    public float averageQuietDuration = RVA.AuroraSettings.DefaultAverageQuietDuration;
    [Range(0f, 1f)] public float minStormPeak = 0.4f;
    [Range(0f, 1f)] public float maxStormPeak = 1.0f;
    public float minRampUpDuration   = 8f;
    public float maxRampUpDuration   = 25f;
    public float minPeakDuration     = 4f;
    public float maxPeakDuration     = 15f;
    public float minRampDownDuration = 15f;
    public float maxRampDownDuration = 40f;

    enum StormPhase { Quiet, RampUp, Peak, RampDown }
    public static readonly string[] PhaseNames = { "Quiet", "RampUp", "Peak", "RampDown" };

    public int PhaseIndex => (int)seq.phase;
    public float AnimationDeltaTime { get; private set; }
    float animTimeAccumulator = 0f;
    double lastAnimationGameTime;
    bool hasAnimationGameTime;

    class Sequence
    {
        public StormPhase phase = StormPhase.Quiet;
        public double phaseStart;
        public float phaseDuration, peak;
        public float mean;
        public System.Random rng = new System.Random();
    }
    static readonly System.Collections.Generic.Dictionary<int, Sequence> sequences
        = new System.Collections.Generic.Dictionary<int, Sequence>();
    Sequence seq = new Sequence();
    bool attached;

    void OnEnable()
    {
        if (!attached) ResetSchedule();
    }

    public void Seed(int seed)
    {
        if (!sequences.TryGetValue(seed, out seq))
        {

            seq = new Sequence { rng = new System.Random(seed ^ System.Environment.TickCount) };
            sequences[seed] = seq;
            ResetSchedule();
            seq.phaseDuration = SampleFirstWait(averageQuietDuration);
        }
        attached = true;
        currentStormIntensity = 0f;
    }

    void ResetSchedule()
    {
        seq.phase = StormPhase.Quiet;
        currentStormIntensity = 0f;
        seq.phaseStart = GameTimeNow();
        seq.mean = averageQuietDuration;
        seq.phaseDuration = SampleQuietInterval(averageQuietDuration);
    }

    public void TriggerStorm()
    {
        if (seq.phase != StormPhase.Quiet) return;
        seq.phaseStart = GameTimeNow() - seq.phaseDuration;
        manualOverride = false;
    }

    public string Status
    {
        get
        {
            double now = GameTimeNow();
            float left = Mathf.Max(0f, seq.phaseDuration - (float)(now - seq.phaseStart));
            switch (seq.phase)
            {
                case StormPhase.Quiet:  return $"quiet, next storm in {FormatGameDuration(left)}";
                case StormPhase.RampUp: return $"storm rising to {seq.peak:F2}, at peak in {left:F0} s";
                case StormPhase.Peak:   return $"storm at its peak {seq.peak:F2} for {left:F0} s more";
                default:                return $"storm fading, over in {left:F0} s";
            }
        }
    }

    void Update()
    {

        if (auroraMaterial != null && transform.parent != null)
        {
            Vector3 c = transform.parent.position;
            auroraMaterial.SetVector("_PlanetCenter", new Vector4(c.x, c.y, c.z, 0f));
        }

        float dt = Application.isPlaying ? GetAnimationDeltaTime() : 0.016f;
        AnimationDeltaTime = dt;
        float currentSpeed = Mathf.Lerp(baseMatAnimSpeed, stormMatAnimSpeed, currentStormIntensity);
        animTimeAccumulator += dt * currentSpeed;
        if (auroraMaterial != null)
            auroraMaterial.SetFloat("_AnimTime", animTimeAccumulator);

        if (Application.isPlaying)
        {
            if (!manualOverride) UpdateStormState();
        }

        ApplyParameters(currentStormIntensity);
    }

    float GetAnimationDeltaTime()
    {
        double gameNow = GameTimeNow();
        float wallDelta = Time.unscaledDeltaTime;
        if (!hasAnimationGameTime)
        {
            lastAnimationGameTime = gameNow;
            hasAnimationGameTime = true;
            return Time.deltaTime;
        }

        double gameDelta = gameNow - lastAnimationGameTime;
        lastAnimationGameTime = gameNow;
        if (gameDelta <= 0d || wallDelta <= 0f) return 0f;

        return Mathf.Min((float)gameDelta, wallDelta * MaxAnimationWarpRate);
    }

    void UpdateStormState()
    {
        double gameNow = GameTimeNow();
        switch (seq.phase)
        {
            case StormPhase.Quiet:
            {
                double elapsed = gameNow - seq.phaseStart;
                if (elapsed < 0d)
                {
                    seq.phaseStart = gameNow;
                    elapsed = 0d;
                }
                currentStormIntensity = 0f;
                if (averageQuietDuration != seq.mean && averageQuietDuration > 0f && seq.mean > 0f)
                {

                    float frac = seq.phaseDuration > 0f ? Mathf.Clamp01((float)(elapsed / seq.phaseDuration)) : 1f;
                    seq.phaseDuration *= averageQuietDuration / seq.mean;
                    seq.phaseStart     = gameNow - frac * seq.phaseDuration;
                    seq.mean           = averageQuietDuration;
                    elapsed            = gameNow - seq.phaseStart;
                }
                if (elapsed >= seq.phaseDuration)
                {
                    seq.phase         = StormPhase.RampUp;
                    seq.phaseStart    = gameNow;
                    seq.phaseDuration = RandRange(minRampUpDuration, maxRampUpDuration);
                    seq.peak          = RandRange(minStormPeak, maxStormPeak);
                }
                break;
            }

            case StormPhase.RampUp:
            {
                double elapsed = gameNow - seq.phaseStart;
                float frac = Mathf.Clamp01((float)(elapsed / seq.phaseDuration));
                currentStormIntensity = Mathf.SmoothStep(0f, seq.peak, frac);
                if (frac >= 1f)
                {
                    seq.phase         = StormPhase.Peak;
                    seq.phaseStart    = gameNow;
                    seq.phaseDuration = RandRange(minPeakDuration, maxPeakDuration);
                }
                break;
            }

            case StormPhase.Peak:
                currentStormIntensity = seq.peak;
                if (gameNow - seq.phaseStart >= seq.phaseDuration)
                {
                    seq.phase         = StormPhase.RampDown;
                    seq.phaseStart    = gameNow;
                    seq.phaseDuration = RandRange(minRampDownDuration, maxRampDownDuration);
                }
                break;

            case StormPhase.RampDown:
            {
                double elapsed = gameNow - seq.phaseStart;
                float frac = Mathf.Clamp01((float)(elapsed / seq.phaseDuration));
                currentStormIntensity = Mathf.SmoothStep(seq.peak, 0f, frac);
                if (frac >= 1f)
                {
                    seq.phase         = StormPhase.Quiet;
                    seq.phaseStart    = gameNow;
                    seq.mean          = averageQuietDuration;
                    seq.phaseDuration = SampleQuietInterval(averageQuietDuration);
                }
                break;
            }
        }
    }

    void ApplyParameters(float i)
    {
        if (auroraMaterial != null)
        {
            auroraMaterial.SetFloat("_Activity",  Mathf.Lerp(baseMatActivity,  stormMatActivity,  i));
            auroraMaterial.SetFloat("_AnimSpeed", Mathf.Lerp(baseMatAnimSpeed, stormMatAnimSpeed, i));
            auroraMaterial.SetFloat("_AltTop",    Mathf.Lerp(baseMatAltTop,    stormMatAltTop,    i) * worldScale);
            if (driveOval)
            {

                auroraMaterial.SetFloat("_LatCenter",   Mathf.Lerp(baseLatCenter,   stormLatCenter,   i));
                auroraMaterial.SetFloat("_BandSpacing", Mathf.Lerp(baseBandSpacing, stormBandSpacing, i));
                auroraMaterial.SetFloat("_LatWidth",    Mathf.Lerp(baseLatWidth,    stormLatWidth,    i));
                auroraMaterial.SetFloat("_FoldStrength", Mathf.Lerp(baseFoldStrength, stormFoldStrength, i));
                auroraMaterial.SetFloat("_Arc2Fade",    ArcFade(arc2Threshold, i));
                auroraMaterial.SetFloat("_Arc3Fade",    ArcFade(arc3Threshold, i));
            }
        }
    }

    public static float ArcFade(float threshold, float intensity)
    {
        if (threshold <= 0.0005f) return 1f;
        float u = Mathf.Clamp01((intensity - (threshold - 0.15f)) / 0.15f);
        return u * u * (3f - 2f * u);
    }

    static string FormatGameDuration(float seconds)
    {
        float days = seconds / RVA.AuroraSettings.GameDaySeconds;
        if (days >= 60f) return $"{days / 30f:F1} game months";
        if (days >= 7f) return $"{days / 7f:F1} game weeks";
        return $"{days:F0} game days";
    }

    static double GameTimeNow()
    {
        return Application.isPlaying
            ? Planetarium.GetUniversalTime()
#if UNITY_EDITOR
            : UnityEditor.EditorApplication.timeSinceStartup;
#else
            : 0f;
#endif
    }

    float RandRange(float a, float b) => a + (float)seq.rng.NextDouble() * (b - a);

    float SampleQuietInterval(float mean)
    {
        float u = Mathf.Max(0.001f, (float)seq.rng.NextDouble());
        return mean * (0.4f + 0.6f * -Mathf.Log(u));
    }

    float SampleFirstWait(float mean)
    {
        if (seq.rng.NextDouble() < 0.4) return mean * 0.4f * (float)seq.rng.NextDouble();
        float u = Mathf.Max(0.001f, (float)seq.rng.NextDouble());
        return mean * (0.4f + 0.6f * -Mathf.Log(u));
    }

    public void SetStormIntensity(float i) { currentStormIntensity = Mathf.Clamp01(i); manualOverride = true; }
    public void ResumeAutomaticScheduler() { manualOverride = false; }
}
