using System;
using System.IO;
using GravityPuzzle.Creative.Runtime;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace GravityPuzzle.Creative.EditorTools
{
    /// <summary>
    /// Records a creative take with Unity Recorder (editor only).
    ///
    /// Active only when a take request exists: the request is peeked (not consumed) right after the
    /// Play Mode domain reload, before the scene's composition root consumes it. Recording starts at
    /// EnteredPlayMode and stops at ExitingPlayMode (CreativeSession.End exits Play Mode after the
    /// take's tail). The driver then writes <c>&lt;take&gt;.take.json</c>, which maps event frames to
    /// video frames: <c>video_frame = unity_frame - recording_start_unity_frame</c>.
    /// Normal Play Mode is unaffected: without a request this class does nothing.
    /// </summary>
    [InitializeOnLoad]
    public static class CreativeRecorderDriver
    {
        private const string TakeSchema = "creative-pipeline.take/1";

        private static CreativeTakeRequest.Data pending;
        private static RecorderController controller;
        private static RecorderControllerSettings controllerSettings;
        private static MovieRecorderSettings movieSettings;
        private static TakeRecord record;

        static CreativeRecorderDriver()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // With domain reload enabled (the project default) this static constructor runs again
            // after the reload into Play Mode, before any scene Awake.
            if (EditorApplication.isPlayingOrWillChangePlaymode && pending == null)
                pending = CreativeTakeRequest.PeekPendingData();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // Covers projects that disable domain reload on Play.
                    pending = CreativeTakeRequest.PeekPendingData();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    if (pending != null)
                        StartRecording(pending);
                    pending = null;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    StopRecording();
                    break;
            }
        }

        private static void StartRecording(CreativeTakeRequest.Data request)
        {
            if (request.captureWidth <= 0 || request.captureHeight <= 0 || request.captureFrameRate <= 0)
            {
                Debug.LogError($"[CreativeRecorder] Take '{request.takeName}' has an invalid capture size or frame rate; not recording.");
                return;
            }

            Directory.CreateDirectory(request.outputFolder);
            Vector2 gameView = Handles.GetMainGameViewSize();

            movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "Creative Take";
            movieSettings.Enabled = true;
            movieSettings.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = request.captureWidth,
                OutputHeight = request.captureHeight,
            };
            movieSettings.AudioInputSettings.PreserveAudio = true;
            movieSettings.OutputFile = Path.Combine(request.outputFolder, request.takeName); // Recorder adds ".mp4"

            controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(movieSettings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            controllerSettings.FrameRate = request.captureFrameRate;
            controllerSettings.CapFrameRate = true;

            controller = new RecorderController(controllerSettings);
            controller.PrepareRecording();
            if (!controller.StartRecording())
            {
                Debug.LogError($"[CreativeRecorder] Unity Recorder failed to start for take '{request.takeName}'; see the errors above. No take.json is written.");
                ReleaseRecorder();
                return;
            }

            record = new TakeRecord
            {
                schema = TakeSchema,
                take = request.takeName,
                config_path = request.configPath,
                video = request.takeName + ".mp4",
                events = request.takeName + ".events.json",
                capture = new Capture
                {
                    width = request.captureWidth,
                    height = request.captureHeight,
                    fps = request.captureFrameRate,
                },
                game_view_at_start = new Capture { width = (int)gameView.x, height = (int)gameView.y, fps = 0 },
                recording_start_unity_frame = Time.frameCount,
                recording_start_rendered_frame = Time.renderedFrameCount,
                unity_version = Application.unityVersion,
                recorder = "com.unity.recorder 4.0.3, MovieRecorder H.264 MP4 (High), Game View input, constant frame rate, capped",
            };
            Debug.Log($"[CreativeRecorder] Recording '{request.takeName}' at {request.captureWidth}x{request.captureHeight} @ {request.captureFrameRate} fps from Unity frame {record.recording_start_unity_frame}.");
        }

        private static void StopRecording()
        {
            if (controller == null)
                return;

            int stopFrame = Time.frameCount;
            if (controller.IsRecording())
                controller.StopRecording();

            if (record != null)
            {
                record.recording_stop_unity_frame = stopFrame;
                string folder = Path.GetDirectoryName(movieSettings.OutputFile);
                string path = Path.Combine(folder ?? string.Empty, record.take + ".take.json");
                File.WriteAllText(path, JsonUtility.ToJson(record, true));
                Debug.Log($"[CreativeRecorder] Stopped at Unity frame {stopFrame}; wrote {path}.");
            }

            ReleaseRecorder();
        }

        private static void ReleaseRecorder()
        {
            if (movieSettings != null)
                UnityEngine.Object.DestroyImmediate(movieSettings);
            if (controllerSettings != null)
                UnityEngine.Object.DestroyImmediate(controllerSettings);
            movieSettings = null;
            controllerSettings = null;
            controller = null;
            record = null;
        }

        [Serializable]
        private sealed class Capture
        {
            public int width;
            public int height;
            public int fps;
        }

        // Field names are the JSON keys read by creative-pipeline (cpl/takes.py).
        [Serializable]
        private sealed class TakeRecord
        {
            public string schema;
            public string take;
            public string config_path;
            public string video;
            public string events;
            public Capture capture;
            public Capture game_view_at_start;
            public int recording_start_unity_frame;
            public int recording_start_rendered_frame;
            public int recording_stop_unity_frame;
            public string unity_version;
            public string recorder;
        }
    }
}
