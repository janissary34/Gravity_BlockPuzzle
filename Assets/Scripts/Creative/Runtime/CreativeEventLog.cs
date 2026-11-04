using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GravityPuzzle.Config;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle.Creative.Runtime
{
    public enum CreativeEventKind : byte
    {
        TakeStart = 0,
        LevelReady = 1,
        GameState = 2,
        PieceState = 3,
        PieceShredded = 4,
        LevelCleared = 5,
        LevelFailed = 6,
        BoosterUsed = 7,
        TakeEnd = 8,
        PieceReleased = 9,
        ScriptStart = 10,
        CaptureReady = 11,
    }

    /// <summary>
    /// Frame-stamped gameplay events of one creative take, written as events.json
    /// (schema "creative-pipeline.events/1"). Events are stored as small structs in a
    /// preallocated list; strings are produced only once, when the file is written.
    /// <c>unity_frame</c> is <see cref="Time.frameCount"/>; with Unity Recorder capturing at a
    /// constant frame rate every Unity frame is one video frame, so the pipeline maps an event to
    /// a video frame as <c>unity_frame - recording_start_unity_frame</c> (from the recorder's take.json).
    /// </summary>
    public sealed class CreativeEventLog
    {
        private struct Entry
        {
            public int Frame;
            public float Time;
            public CreativeEventKind Kind;
            public int PieceId;
            public int From;
            public int To;
            public int Value;
        }

        private static readonly string[] KindNames =
        {
            "take.start", "level.ready", "game.state", "piece.state", "piece.shredded",
            "level.cleared", "level.failed", "booster.used", "take.end", "piece.released", "script.start", "capture.ready",
        };

        private readonly List<Entry> entries;

        public CreativeEventLog(int capacity)
        {
            entries = new List<Entry>(Math.Max(16, capacity));
        }

        public int Count => entries.Count;

        public void Add(CreativeEventKind kind, int pieceId = -1, int from = -1, int to = -1, int value = 0)
        {
            entries.Add(new Entry
            {
                Frame = UnityEngine.Time.frameCount,
                Time = UnityEngine.Time.time,
                Kind = kind,
                PieceId = pieceId,
                From = from,
                To = to,
                Value = value,
            });
        }

        /// <summary>Writes events.json. Enum values are written by name (game.state and piece.state from/to).</summary>
        public void Write(string path, string takeName, string levelName, int width, int height, int fps)
        {
            var json = new StringBuilder(256 + entries.Count * 96);
            json.Append("{\n  \"schema\": \"creative-pipeline.events/1\",\n  \"game\": \"gravity-blockpuzzle\",\n");
            json.Append("  \"take\": ").Append(Quote(takeName)).Append(",\n");
            json.Append("  \"level\": ").Append(Quote(levelName)).Append(",\n");
            json.Append("  \"capture\": {\"width\": ").Append(width).Append(", \"height\": ").Append(height)
                .Append(", \"fps\": ").Append(fps).Append("},\n");
            json.Append("  \"clock\": \"unity_frame = Time.frameCount; t = Time.time (seconds of game time)\",\n");
            json.Append("  \"events\": [\n");
            for (int index = 0; index < entries.Count; index++)
            {
                Entry e = entries[index];
                json.Append("    {\"unity_frame\": ").Append(e.Frame)
                    .Append(", \"t\": ").Append(e.Time.ToString("0.#####", CultureInfo.InvariantCulture))
                    .Append(", \"type\": ").Append(Quote(KindNames[(int)e.Kind]));
                if (e.PieceId >= 0)
                    json.Append(", \"piece_id\": ").Append(e.PieceId);
                if (e.Kind == CreativeEventKind.PieceState)
                    json.Append(", \"from\": ").Append(Quote(((PieceState)e.From).ToString()))
                        .Append(", \"to\": ").Append(Quote(((PieceState)e.To).ToString()));
                else if (e.Kind == CreativeEventKind.GameState)
                    json.Append(", \"from\": ").Append(Quote(((GameState)e.From).ToString()))
                        .Append(", \"to\": ").Append(Quote(((GameState)e.To).ToString()));
                else if (e.Kind == CreativeEventKind.PieceReleased)
                    json.Append(", \"accepted\": ").Append(e.Value != 0 ? "true" : "false");
                else if (e.Kind == CreativeEventKind.ScriptStart)
                    json.Append(", \"drags\": ").Append(e.Value);
                else if (e.Kind == CreativeEventKind.BoosterUsed)
                    json.Append(", \"booster\": ").Append(Quote(((BoosterRewardType)e.From).ToString()))
                        .Append(", \"remaining\": ").Append(e.Value);
                json.Append(index + 1 < entries.Count ? "},\n" : "}\n");
            }
            json.Append("  ]\n}\n");

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        private static string Quote(string text)
        {
            if (text == null)
                return "null";
            var quoted = new StringBuilder(text.Length + 2).Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\')
                    quoted.Append('\\').Append(c);
                else if (c < ' ')
                    quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    quoted.Append(c);
            }
            return quoted.Append('"').ToString();
        }
    }
}
