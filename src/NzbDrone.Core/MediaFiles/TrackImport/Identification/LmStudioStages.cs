using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    // Work only on JSON until every stage succeeds. The caller applies one globally validated result.
    internal sealed class LmStudioStages
    {
        private readonly ILmStudioClient _client;
        private readonly LmStudioSettings _settings;
        private readonly Logger _logger;
        private int _requests;

        public LmStudioStages(ILmStudioClient client, LmStudioSettings settings, Logger logger)
        {
            _client = client;
            _settings = settings;
            _logger = logger;
        }

        public JObject Complete(JObject input)
        {
            var answer = Evaluate(input, 0);
            Validate(input, answer);
            return answer;
        }

        private JObject Evaluate(JObject input, int depth)
        {
            if (depth > 24)
            {
                throw new InvalidDataException("AI staging could not resolve the release within its size limit; narrow the release in Manual Import");
            }

            var files = (JArray)input["files"];
            var candidates = (JArray)input["candidates"];
            var payload = LmStudioMatcher.BuildRequest(input, _settings);
            var size = payload.ToString(Formatting.None).Length;
            var outputLimited = false;
            if (files.Count <= _settings.StageFileCount && size <= Math.Min(_settings.StageRequestCharacters, _settings.MaxRequestCharacters))
            {
                if (++_requests > 128)
                {
                    throw new InvalidDataException("AI staging reached its 128-request safety limit; narrow the release in Manual Import");
                }

                _logger.Info("AI matching stage {0}: {1} files, {2} candidate releases, {3} request characters", _requests, files.Count, candidates.Count, size);
                try
                {
                    var response = _client.Complete(payload, _settings);
                    var choice = response["choices"]?.SingleOrDefault();
                    if (choice?["message"]?["refusal"]?.Type == JTokenType.String)
                    {
                        throw new InvalidDataException("LM Studio refused the AI matching request");
                    }

                    if ((string)choice?["finish_reason"] == "length")
                    {
                        outputLimited = true;
                    }
                    else
                    {
                        if ((string)choice?["finish_reason"] != "stop")
                        {
                            throw new InvalidDataException("LM Studio did not return a complete, non-refused answer");
                        }

                        var answer = JObject.Parse((string)choice["message"]["content"] ?? string.Empty);
                        Validate(input, answer);
                        return answer;
                    }
                }
                catch (LmStudioContextException)
                {
                    _logger.Info("LM Studio context limit reached; splitting AI matching stage");
                }
            }

            // Split output-heavy requests by files first; otherwise reduce competing releases first.
            if (files.Count > 1 && (outputLimited || files.Count > _settings.StageFileCount || candidates.Count == 1))
            {
                var middle = files.Count / 2;
                var left = Slice(input, "files", files.Take(middle));
                var right = Slice(input, "files", files.Skip(middle));
                var answers = new[] { Evaluate(left, depth + 1), Evaluate(right, depth + 1) };
                return MergeFiles(input, answers);
            }

            if (candidates.Count > 1)
            {
                var middle = candidates.Count / 2;
                var left = Slice(input, "candidates", candidates.Take(middle));
                var right = Slice(input, "candidates", candidates.Skip(middle));
                return Compare(input, new[] { Evaluate(left, depth + 1), Evaluate(right, depth + 1) }, depth + 1);
            }

            // Even a box-set track list can exceed context. Preserve global track tokens across pages.
            var tracks = candidates[0]?["tracks"] as JArray;
            if (!outputLimited && tracks?.Count > 1)
            {
                var middle = tracks.Count / 2;
                var left = TrackPage(input, tracks.Take(middle));
                var right = TrackPage(input, tracks.Skip(middle));
                return Compare(input, new[] { Evaluate(left, depth + 1), Evaluate(right, depth + 1) }, depth + 1);
            }

            throw new InvalidDataException(outputLimited
                ? "LM Studio exhausted output tokens for one file; use a model that returns concise JSON"
                : "One AI file/track stage exceeds the request or context size limit; narrow metadata in Manual Import or increase LM Studio context");
        }

        private static JObject Slice(JObject input, string key, IEnumerable<JToken> values)
        {
            var result = (JObject)input.DeepClone();
            result[key] = new JArray(values.Select(v => v.DeepClone()));
            return result;
        }

        private static JObject TrackPage(JObject input, IEnumerable<JToken> tracks)
        {
            var result = (JObject)input.DeepClone();
            var candidate = result["candidates"][0];
            candidate["totalTrackCount"] = candidate["totalTrackCount"] ?? ((JArray)candidate["tracks"]).Count;
            candidate["partialTrackList"] = true;
            candidate["tracks"] = new JArray(tracks.Select(t => t.DeepClone()));
            return result;
        }

        private static JObject MergeFiles(JObject input, JObject[] answers)
        {
            var ids = answers.Select(a => (string)a["candidateId"]).Distinct().ToList();
            if (ids.Count != 1 || ids[0] == null)
            {
                return Unresolved(input, answers, "File stages did not agree on a single release; manual review required");
            }

            var result = (JObject)answers[0].DeepClone();
            result["confidence"] = answers.Min(a => LmStudioMatcher.Confidence(a["confidence"]));
            result["files"] = new JArray(answers.SelectMany(a => (JArray)a["files"]).Select(r => r.DeepClone()));
            Validate(input, result); // Includes duplicate track assignments ACROSS file stages.
            return result;
        }

        private JObject Compare(JObject input, JObject[] answers, int depth)
        {
            // An uncertain discarded branch may contain the correct release. Never silently eliminate it.
            if (answers.Any(a => a["candidateId"].Type == JTokenType.Null && ((JArray)a["files"]).Any(r => (string)r["status"] != "no_match")))
            {
                return Unresolved(input, answers, "A candidate stage was uncertain; manual review required");
            }

            var viable = answers.Where(a => a["candidateId"].Type == JTokenType.String).ToArray();
            if (viable.Length == 0)
            {
                return Unresolved(input, answers, "No matching release found across AI stages");
            }

            if (viable.Length == 1)
            {
                return viable[0];
            }

            // Recompare ONLY alternatives actually proposed by validated stages. Include original file
            // evidence, release identity and proposed track metadata, not repeated full release lists.
            var comparison = (JObject)input.DeepClone();
            var choices = new JArray();
            foreach (var group in viable.GroupBy(a => (string)a["candidateId"]))
            {
                var candidate = (JObject)((JArray)input["candidates"]).Single(c => (string)c["candidateId"] == group.Key).DeepClone();
                var ids = group.SelectMany(a => (JArray)a["files"]).Select(r => (string)r["trackId"]).Where(id => id != null).ToHashSet();
                candidate["totalTrackCount"] = candidate["totalTrackCount"] ?? ((JArray)candidate["tracks"]).Count;
                candidate["partialTrackList"] = true;
                candidate["tracks"] = new JArray(((JArray)candidate["tracks"]).Where(t => ids.Contains((string)t["trackId"])).Select(t => t.DeepClone()));
                choices.Add(candidate);
            }

            comparison["candidates"] = choices;
            comparison["stage"] = "Compare competing proposals against original file evidence; return null if alternatives cannot be distinguished";

            // Bounded reasons prevent verbose model explanations expanding the next prompt.
            comparison["assessments"] = new JArray(viable.Select(a => new JObject
            {
                ["candidateId"] = a["candidateId"].DeepClone(),
                ["confidence"] = a["confidence"].DeepClone(),
                ["reason"] = ShortReason(a["reason"])
            }));
            var result = Evaluate(comparison, depth);
            var selected = viable.Where(a => (string)a["candidateId"] == (string)result["candidateId"]).ToArray();
            if (selected.Length != 0)
            {
                result["confidence"] = Math.Min(LmStudioMatcher.Confidence(result["confidence"]), selected.Min(a => LmStudioMatcher.Confidence(a["confidence"])));
                foreach (var row in (JArray)result["files"])
                {
                    if ((string)row["status"] != "match")
                    {
                        continue;
                    }

                    var proposals = selected.SelectMany(a => (JArray)a["files"])
                        .Where(r => (string)r["fileId"] == (string)row["fileId"] && (string)r["trackId"] == (string)row["trackId"]).ToList();
                    row["confidence"] = proposals.Count == 0 ? 0 : Math.Min(LmStudioMatcher.Confidence(row["confidence"]), proposals.Min(r => LmStudioMatcher.Confidence(r["confidence"])));
                }
            }

            Validate(input, result);
            return result;
        }

        private static string ShortReason(JToken reason)
        {
            var text = LmStudioMatcher.Reason(reason);
            return text.Length > 160 ? text.Substring(0, 160) : text;
        }

        private static JObject Unresolved(JObject input, JObject[] answers, string reason)
        {
            return new JObject
            {
                ["candidateId"] = null,
                ["confidence"] = 0,
                ["reason"] = reason,
                ["files"] = new JArray(((JArray)input["files"]).Select(f =>
                {
                    var rows = answers.SelectMany(a => (JArray)a["files"]).Where(r => (string)r["fileId"] == (string)f["fileId"]).ToList();
                    return new JObject
                    {
                        ["fileId"] = f["fileId"].DeepClone(),
                        ["trackId"] = null,
                        ["status"] = rows.Count > 0 && rows.All(r => (string)r["status"] == "no_match") ? "no_match" : "review",
                        ["confidence"] = 0,
                        ["reason"] = reason
                    };
                }))
            };
        }

        private static void Validate(JObject input, JObject answer)
        {
            LmStudioMatcher.Confidence(answer["confidence"]);
            LmStudioMatcher.Reason(answer["reason"]);
            var token = answer["candidateId"];
            if (token == null || (token.Type != JTokenType.String && token.Type != JTokenType.Null))
            {
                throw new InvalidDataException("AI stage returned an invalid release candidate token");
            }

            var candidate = ((JArray)input["candidates"]).SingleOrDefault(c => (string)c["candidateId"] == (string)token);
            if (token.Type == JTokenType.String && candidate == null)
            {
                throw new InvalidDataException("AI stage selected a release outside the supplied candidates");
            }

            var files = ((JArray)input["files"]).Select(f => (string)f["fileId"]).ToHashSet();
            var tracks = candidate == null ? new HashSet<string>() : ((JArray)candidate["tracks"]).Select(t => (string)t["trackId"]).ToHashSet();
            var rows = answer["files"] as JArray;
            if (rows == null || rows.Count != files.Count)
            {
                throw new InvalidDataException("AI stage must cover every supplied file exactly once");
            }

            var seenFiles = new HashSet<string>();
            var seenTracks = new HashSet<string>();
            foreach (var row in rows)
            {
                LmStudioMatcher.Confidence(row["confidence"]);
                LmStudioMatcher.Reason(row["reason"]);
                var fileId = (string)row["fileId"];
                var track = row["trackId"];
                var status = (string)row["status"];
                if (!files.Contains(fileId) || !seenFiles.Add(fileId) || track == null ||
                    (track.Type != JTokenType.String && track.Type != JTokenType.Null) ||
                    (status != "match" && status != "review" && status != "no_match") ||
                    (status == "match") != (track.Type == JTokenType.String) ||
                    (track.Type == JTokenType.String && (!tracks.Contains((string)track) || !seenTracks.Add((string)track))))
                {
                    throw new InvalidDataException("AI returned invalid, duplicate, or cross-release file/track tokens");
                }
            }
        }
    }
}
