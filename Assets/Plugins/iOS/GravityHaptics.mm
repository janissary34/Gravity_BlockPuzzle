// iOS backend for GravityPuzzle.Infrastructure.Services.DeviceHaptics.
// Core Haptics (iOS 13+, iPhone 8+) plays the authored intensity/duration cues.
// Devices without Core Haptics fall back to UIImpactFeedbackGenerator one-shots.
// CoreHaptics is weak-linked by Assets/Editor/IosHapticsPostProcessBuild.cs.
// All entry points are called from Unity's main thread.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>

#if !__has_feature(objc_arc)
#error "GravityHaptics.mm must be compiled with ARC (-fobjc-arc)."
#endif

static CHHapticEngine *engine API_AVAILABLE(ios(13.0));
static id<CHHapticAdvancedPatternPlayer> loopPlayer API_AVAILABLE(ios(13.0));
static id<CHHapticPatternPlayer> oneShotPlayer API_AVAILABLE(ios(13.0));
static BOOL engineRunning;
static BOOL capabilityChecked;
static BOOL coreHapticsSupported;
static BOOL engineFailed;

static BOOL SupportsCoreHaptics(void)
{
    if (!capabilityChecked)
    {
        capabilityChecked = YES;
        if (@available(iOS 13.0, *))
            coreHapticsSupported = CHHapticEngine.capabilitiesForHardware.supportsHaptics;
    }
    return coreHapticsSupported && !engineFailed;
}

API_AVAILABLE(ios(13.0))
static void ForgetPlayers(void)
{
    loopPlayer = nil;
    oneShotPlayer = nil;
}

API_AVAILABLE(ios(13.0))
static BOOL EnsureEngineRunning(void)
{
    NSError *error = nil;
    if (engine == nil)
    {
        engine = [[CHHapticEngine alloc] initAndReturnError:&error];
        if (engine == nil)
        {
            // Creation failure is a hardware/OS condition; stop retrying every cue.
            engineFailed = YES;
            NSLog(@"[Haptics] CHHapticEngine creation failed: %@", error);
            return NO;
        }

        engine.playsHapticsOnly = YES;
        // The system stops the engine on backgrounding or audio interruption;
        // the next cue restarts it lazily.
        engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
            dispatch_async(dispatch_get_main_queue(), ^{
                engineRunning = NO;
                ForgetPlayers();
            });
        };
        engine.resetHandler = ^{
            dispatch_async(dispatch_get_main_queue(), ^{
                engineRunning = NO;
                ForgetPlayers();
            });
        };
    }

    if (!engineRunning)
    {
        if (![engine startAndReturnError:&error])
        {
            NSLog(@"[Haptics] CHHapticEngine start failed: %@", error);
            return NO;
        }
        engineRunning = YES;
    }
    return YES;
}

API_AVAILABLE(ios(13.0))
static CHHapticEvent *MakeEvent(float intensity, float sharpness, NSTimeInterval time, NSTimeInterval duration)
{
    NSArray<CHHapticEventParameter *> *parameters = @[
        [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:intensity],
        [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:sharpness]
    ];
    return [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                         parameters:parameters
                                       relativeTime:time
                                           duration:duration];
}

API_AVAILABLE(ios(13.0))
static CHHapticPattern *MakePattern(NSArray<CHHapticEvent *> *events)
{
    NSError *error = nil;
    CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&error];
    if (pattern == nil)
        NSLog(@"[Haptics] CHHapticPattern creation failed: %@", error);
    return pattern;
}

API_AVAILABLE(ios(13.0))
static void PlayOneShot(NSArray<CHHapticEvent *> *events)
{
    if (events.count == 0 || !EnsureEngineRunning())
        return;

    CHHapticPattern *pattern = MakePattern(events);
    if (pattern == nil)
        return;

    NSError *error = nil;
    id<CHHapticPatternPlayer> player = [engine createPlayerWithPattern:pattern error:&error];
    if (player == nil || ![player startAtTime:CHHapticTimeImmediate error:&error])
    {
        NSLog(@"[Haptics] One-shot playback failed: %@", error);
        return;
    }
    oneShotPlayer = player;
}

API_AVAILABLE(ios(13.0))
static void StopLoop(void)
{
    if (loopPlayer == nil)
        return;
    [loopPlayer stopAtTime:CHHapticTimeImmediate error:nil];
    loopPlayer = nil;
}

static void FallbackImpact(float intensity)
{
    if (intensity <= 0.0f)
        return;
    UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
    if (@available(iOS 13.0, *))
        [generator impactOccurredWithIntensity:MIN(intensity, 1.0f)];
    else
        [generator impactOccurred];
}

static NSTimeInterval Seconds(int milliseconds)
{
    return MAX(milliseconds, 0) / 1000.0;
}

extern "C"
{
    void GravityHaptics_Pulse(float intensity, int durationMilliseconds, float sharpness)
    {
        if (intensity <= 0.0f)
            return;

        if (@available(iOS 13.0, *))
        {
            if (SupportsCoreHaptics())
            {
                PlayOneShot(@[MakeEvent(intensity, sharpness, 0.0, Seconds(durationMilliseconds))]);
                return;
            }
        }
        FallbackImpact(intensity);
    }

    void GravityHaptics_Pattern(float intensityA, int durationA,
                                float intensityB, int durationB,
                                float intensityC, int durationC,
                                int gapMilliseconds, float sharpness)
    {
        const float intensities[3] = { intensityA, intensityB, intensityC };
        const int durations[3] = { durationA, durationB, durationC };

        if (@available(iOS 13.0, *))
        {
            if (SupportsCoreHaptics())
            {
                NSMutableArray<CHHapticEvent *> *events = [NSMutableArray arrayWithCapacity:3];
                NSTimeInterval time = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    // A disabled pulse arrives with zero intensity but still occupies its slot,
                    // matching the Android waveform timing.
                    if (intensities[i] > 0.0f)
                        [events addObject:MakeEvent(intensities[i], sharpness, time, Seconds(durations[i]))];
                    time += Seconds(durations[i]) + Seconds(gapMilliseconds);
                }
                PlayOneShot(events);
                return;
            }
        }

        NSTimeInterval time = 0.0;
        for (int i = 0; i < 3; i++)
        {
            const float intensity = intensities[i];
            if (intensity > 0.0f)
            {
                dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(time * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                    FallbackImpact(intensity);
                });
            }
            time += Seconds(durations[i]) + Seconds(gapMilliseconds);
        }
    }

    void GravityHaptics_StartRepeating(int onMilliseconds, int offMilliseconds, float intensity, float sharpness)
    {
        // Continuous patterns need Core Haptics; impact generators cannot express them.
        if (@available(iOS 13.0, *))
        {
            if (!SupportsCoreHaptics())
                return;

            StopLoop();
            if (intensity <= 0.0f || !EnsureEngineRunning())
                return;

            CHHapticPattern *pattern = MakePattern(@[MakeEvent(intensity, sharpness, 0.0, Seconds(onMilliseconds))]);
            if (pattern == nil)
                return;

            NSError *error = nil;
            id<CHHapticAdvancedPatternPlayer> player = [engine createAdvancedPlayerWithPattern:pattern error:&error];
            if (player == nil)
            {
                NSLog(@"[Haptics] Repeating player creation failed: %@", error);
                return;
            }
            player.loopEnabled = YES;
            player.loopEnd = Seconds(onMilliseconds) + Seconds(offMilliseconds);
            if (![player startAtTime:CHHapticTimeImmediate error:&error])
            {
                NSLog(@"[Haptics] Repeating playback failed: %@", error);
                return;
            }
            loopPlayer = player;
        }
    }

    void GravityHaptics_Cancel(void)
    {
        if (@available(iOS 13.0, *))
        {
            StopLoop();
            if (oneShotPlayer != nil)
            {
                [oneShotPlayer stopAtTime:CHHapticTimeImmediate error:nil];
                oneShotPlayer = nil;
            }
        }
    }
}
