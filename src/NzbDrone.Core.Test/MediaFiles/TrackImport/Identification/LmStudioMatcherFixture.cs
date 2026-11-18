using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Aggregation;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class LmStudioMatcherFixture
    {
        private Mock<ILmStudioSettingsProvider> _settingsProvider;
        private Mock<ILmStudioClient> _client;
        private Mock<IAiMatchAudit> _audit;
        private LmStudioSettings _settings;
        private LmStudioMatcher _matcher;
        private LocalAlbumRelease _local;
        private List<CandidateAlbumRelease> _candidates;
        private JObject _answer;

        [SetUp]
        public void Setup()
        {
            _settings = new LmStudioSettings { BaseUrl = "http://localhost:1234/v1", Model = "test-model", Automatic = true };
            _settingsProvider = new Mock<ILmStudioSettingsProvider>();
            _settingsProvider.Setup(s => s.GetSettings()).Returns(_settings);
            _client = new Mock<ILmStudioClient>();
            _audit = new Mock<IAiMatchAudit>();
            _matcher = new LmStudioMatcher(_settingsProvider.Object, _client.Object, _audit.Object, LogManager.GetLogger("AiMatcherTest"));
            var artist = new Artist { Name = "Artist", Metadata = new ArtistMetadata { ForeignArtistId = "artist-mbid" } };
            var album = new Album { Id = 10, Title = "Album", Artist = artist };
            var tracks = new List<Track>
            {
                new Track { Id = 1, AlbumReleaseId = 20, Title = "Original title", Duration = 180000 },
                new Track { Id = 2, AlbumReleaseId = 20, Title = "Second title", Duration = 190000 }
            };
            _candidates = new List<CandidateAlbumRelease>
            {
                new CandidateAlbumRelease(new AlbumRelease { Id = 20, AlbumId = 10, Title = "Album", Album = album, Tracks = tracks })
            };
            _local = new LocalAlbumRelease(new List<LocalTrack>
            {
                new LocalTrack { Path = "album/file-one.flac", FileTrackInfo = new ParsedTrackInfo { Title = "Completely different filename (M4A Lossless)" } },
                new LocalTrack { Path = "album/file-two.flac", FileTrackInfo = new ParsedTrackInfo { Title = "Second" } }
            });
            _answer = JObject.Parse(@"{'candidateId':'c0','confidence':0.99,'reason':'Consistent album evidence','files':[
                {'fileId':'f0','trackId':'c0t0','status':'match','confidence':0.99,'reason':'Recording ID and duration agree'},
                {'fileId':'f1','trackId':'c0t1','status':'match','confidence':0.99,'reason':'Title, order and duration agree'}
            ]}");
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns(() => JObject.FromObject(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { content = _answer.ToString(Formatting.None) } } }
            }));
        }

        private void Identify()
        {
            _matcher.Identify(_local, _candidates, new List<LocalTrack>());
        }

        private static JObject Input(JObject payload)
        {
            return JObject.Parse((string)payload["messages"][1]["content"]);
        }

        private static JObject Response(JObject answer, string finish = "stop")
        {
            return new JObject
            {
                ["choices"] = new JArray(new JObject
                {
                    ["finish_reason"] = finish,
                    ["message"] = new JObject { ["content"] = answer.ToString(Formatting.None) }
                })
            };
        }

        private static JObject StageAnswer(JObject input)
        {
            var candidate = input["candidates"][0];
            var id = (string)candidate["candidateId"];
            var rows = new JArray(((JArray)input["files"]).Select(f =>
            {
                var fileId = (string)f["fileId"];
                var trackId = string.Concat(id, "t", fileId.AsSpan(1));
                var exists = ((JArray)candidate["tracks"]).Any(t => (string)t["trackId"] == trackId);
                return new JObject
                {
                    ["fileId"] = fileId,
                    ["trackId"] = exists ? trackId : null,
                    ["status"] = exists ? "match" : "no_match",
                    ["confidence"] = 0.99,
                    ["reason"] = "Recording and duration evidence"
                };
            }));
            return new JObject
            {
                ["candidateId"] = rows.Any(r => (string)r["status"] == "match") ? id : null,
                ["confidence"] = 0.99,
                ["reason"] = "Album evidence",
                ["files"] = rows
            };
        }

        private void ConflictingFileStages(Func<JObject, JObject> reconcile, Func<JObject, JObject> remap = null, string reconciliationFinish = "stop", bool refusal = false)
        {
            _settings.StageFileCount = 1;
            var release = _candidates[0].AlbumRelease;
            for (var i = 1; i < 3; i++)
            {
                _candidates.Add(new CandidateAlbumRelease(new AlbumRelease
                {
                    Id = 20 + i, AlbumId = 10, Title = "Edition " + i, Album = release.Album, Tracks = release.Tracks.Value
                }));
            }

            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, _) =>
            {
                var input = Input(payload);
                if ((string)input["stage"] == "album_reconciliation")
                {
                    Assert.That(((JArray)input["files"]).Count, Is.EqualTo(2));
                    Assert.That(((JArray)input["candidates"]).Count, Is.EqualTo(3));
                    Assert.That(((JArray)input["candidates"]).All(c => ((JArray)c["tracks"]).Count == 2), Is.True);
                    Assert.That(payload["response_format"]["json_schema"]["schema"]["properties"]["files"], Is.Null);
                    var response = Response(reconcile(input), reconciliationFinish);
                    if (refusal)
                    {
                        response["choices"][0]["message"]["refusal"] = "Refused";
                    }

                    return response;
                }

                if (input["stage"] != null)
                {
                    Assert.That(((JArray)input["candidates"]).Count, Is.EqualTo(1));
                    Assert.That(((JArray)input["candidates"][0]["tracks"]).Count, Is.EqualTo(2));
                    return Response(remap == null ? StageAnswer(input) : remap(input));
                }

                var candidate = ((JArray)input["candidates"])[(string)input["files"][0]["fileId"] == "f0" ? 0 : 1];
                input["candidates"] = new JArray(candidate.DeepClone());
                return Response(StageAnswer(input));
            });
        }

        private static JObject AlbumSelection(string id, double confidence = 0.99)
        {
            return new JObject { ["candidateId"] = id, ["confidence"] = confidence, ["reason"] = "Album-wide edition evidence" };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void conflicting_file_stages_should_reconcile_all_candidates_and_remap_every_file(bool reviewMode)
        {
            _settings.Automatic = !reviewMode;
            ConflictingFileStages(_ => AlbumSelection("c2"));
            Identify();
            Assert.That(_local.AlbumRelease.Id, Is.EqualTo(22));
            Assert.That(_local.LocalTracks.Select(f => f.AiMatch.TrackId), Is.EqualTo(new[] { "c2t0", "c2t1" }));
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Proposed), Is.True);
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Accepted), Is.EqualTo(!reviewMode));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(5));
        }

        [TestCase(null, 0.99)]
        [TestCase("c2", 0.94)]
        public void unresolved_or_low_confidence_album_reconciliation_should_remain_manual(string id, double confidence)
        {
            ConflictingFileStages(_ => AlbumSelection(id, confidence));
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted && !f.AiMatch.IsError), Is.True);
            Assert.That(_local.AiMatch.Reason, Does.Contain("Album-wide AI reconciliation requires review"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(3));
        }

        [Test]
        public void reconciliation_cannot_invent_candidate_ids()
        {
            ConflictingFileStages(_ => AlbumSelection("invented"));
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted && f.Tracks.Count == 0), Is.True);
            Assert.That(_local.AiMatch.Reason, Does.Contain("outside the supplied candidates"));
        }

        [Test]
        public void duplicate_tracks_during_reconciled_remapping_invalidate_every_file()
        {
            ConflictingFileStages(_ => AlbumSelection("c2"), input =>
            {
                var answer = StageAnswer(input);
                answer["files"][0]["trackId"] = "c2t0";
                return answer;
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted && f.Tracks.Count == 0), Is.True);
        }

        [Test]
        public void later_remap_failure_cannot_apply_earlier_success()
        {
            ConflictingFileStages(_ => AlbumSelection("c2"), input =>
            {
                if ((string)input["files"][0]["fileId"] == "f1")
                {
                    throw new InvalidDataException("Later constrained stage failed");
                }

                return StageAnswer(input);
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => f.Tracks.Count == 0 && !f.AiMatch.Accepted), Is.True);
        }

        [Test]
        public void reconciliation_context_limit_should_stop_with_actionable_review()
        {
            ConflictingFileStages(_ => throw new LmStudioContextException());
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.AiMatch.Reason, Does.Contain("increase LM Studio context"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(3));
        }

        [TestCase("length", false)]
        [TestCase("stop", true)]
        [TestCase("tool_calls", false)]
        public void incomplete_or_refused_reconciliation_cannot_select_release(string finish, bool refusal)
        {
            ConflictingFileStages(_ => AlbumSelection("c2"), reconciliationFinish: finish, refusal: refusal);
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.AiMatch.Reason, Does.Contain("did not complete album-wide reconciliation"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(3));
        }

        [Test]
        public void album_wide_request_limit_must_not_truncate_files_or_candidates()
        {
            _settings.MaxRequestCharacters = 12000;
            foreach (var file in _local.LocalTracks)
            {
                file.FileTrackInfo.Title = new string('x', 7000);
            }

            ConflictingFileStages(_ => AlbumSelection("c2"));
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.AiMatch.Reason, Does.Contain("exceeds the request limit"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(2));
        }

        [Test]
        public void independent_album_selection_can_retain_one_uncertain_file_without_repeating_reconciliation()
        {
            ConflictingFileStages(_ => AlbumSelection("c2", 0.96), input =>
            {
                var answer = StageAnswer(input);
                if ((string)input["files"][0]["fileId"] == "f1")
                {
                    answer["candidateId"] = null;
                    answer["files"][0]["trackId"] = null;
                    answer["files"][0]["status"] = "review";
                }

                return answer;
            });
            Identify();
            Assert.That(_local.AlbumRelease.Id, Is.EqualTo(22));
            Assert.That(_local.AiMatch.Confidence, Is.EqualTo(0.96));
            Assert.That(_local.LocalTracks[0].AiMatch.Accepted, Is.True);
            Assert.That(_local.LocalTracks[1].AiMatch.Accepted, Is.False);
            Assert.That(_local.LocalTracks[1].AiMatch.Status, Is.EqualTo("review"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(5));
        }

        [Test]
        public void all_unmatched_files_should_not_request_album_reconciliation()
        {
            _settings.StageFileCount = 1;
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, _) =>
            {
                var answer = StageAnswer(Input(payload));
                answer["candidateId"] = null;
                answer["files"][0]["status"] = "no_match";
                answer["files"][0]["trackId"] = null;
                return Response(answer);
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.AiMatch.Reason, Does.Contain("No matching release found"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(2));
        }

        [Test]
        public void album_selection_without_any_remapped_tracks_should_explain_mapping_failure()
        {
            ConflictingFileStages(_ => AlbumSelection("c2"), input =>
            {
                var answer = StageAnswer(input);
                answer["candidateId"] = null;
                answer["files"][0]["trackId"] = null;
                answer["files"][0]["status"] = "no_match";
                return answer;
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted), Is.True);
            Assert.That(_local.AiMatch.Reason, Does.Contain("could not produce a consistent file mapping"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(5));
        }

        [Test]
        public void unmatched_file_stage_should_not_erase_identified_release()
        {
            _settings.StageFileCount = 1;
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, _) =>
            {
                var answer = StageAnswer(Input(payload));
                if ((string)answer["files"][0]["fileId"] == "f1")
                {
                    answer["candidateId"] = null;
                    answer["confidence"] = 0;
                    answer["files"][0]["status"] = "no_match";
                    answer["files"][0]["trackId"] = null;
                }

                return Response(answer);
            });
            Identify();
            Assert.That(_local.AlbumRelease.Id, Is.EqualTo(20));
            Assert.That(_local.LocalTracks[0].AiMatch.Accepted, Is.True);
            Assert.That(_local.LocalTracks[1].AiMatch.Accepted, Is.False);
            Assert.That(_local.LocalTracks[1].AiMatch.Status, Is.EqualTo("no_match"));
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(2));
        }

        [Test]
        public void file_stages_should_preserve_global_tokens_and_review_mode()
        {
            _settings.StageFileCount = 1;
            _settings.Automatic = false;
            var seen = new List<string>();
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var input = Input(payload);
                seen.Add((string)input["files"][0]["fileId"]);
                return Response(StageAnswer(input));
            });
            Identify();
            Assert.That(seen, Is.EqualTo(new[] { "f0", "f1" }));
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Proposed && !f.AiMatch.Accepted), Is.True);
            Assert.That(_local.TrackMapping.MBExtra, Is.Empty);
        }

        [Test]
        public void duplicate_track_across_file_stages_should_reject_entire_release()
        {
            _settings.StageFileCount = 1;
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var answer = StageAnswer(Input(payload));
                answer["files"][0]["trackId"] = "c0t0";
                return Response(answer);
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.IsError && !f.Tracks.Any()), Is.True);
        }

        [Test]
        public void later_stage_failure_should_never_apply_earlier_success()
        {
            _settings.StageFileCount = 1;
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var input = Input(payload);
                if ((string)input["files"][0]["fileId"] == "f1")
                {
                    throw new TimeoutException();
                }

                return Response(StageAnswer(input));
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.Tracks.Any() && !f.AiMatch.Accepted), Is.True);
        }

        [Test]
        public void output_limit_should_retry_smaller_file_stages()
        {
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var input = Input(payload);
                return Response(StageAnswer(input), ((JArray)input["files"]).Count > 1 ? "length" : "stop");
            });
            Identify();
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Accepted), Is.True);
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Exactly(3));
        }

        [Test]
        public void context_limit_should_page_large_track_lists_without_renumbering()
        {
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var input = Input(payload);
                if (((JArray)input["candidates"][0]["tracks"]).Count > 1)
                {
                    throw new LmStudioContextException();
                }

                return Response(StageAnswer(input));
            });
            Identify();
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Accepted), Is.True);
            Assert.That(_local.LocalTracks[1].AiMatch.TrackId, Is.EqualTo("c0t1"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void candidate_stages_should_compare_finalists_and_preserve_uncertainty(bool uncertain)
        {
            var release = _candidates[0].AlbumRelease;
            _candidates.Add(new CandidateAlbumRelease(new AlbumRelease
            {
                Id = 21, AlbumId = 10, Title = "Other edition", Album = release.Album, Tracks = release.Tracks.Value
            }));
            var compared = false;
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var input = Input(payload);
                if (((JArray)input["candidates"]).Count > 1 && input["stage"] == null)
                {
                    throw new LmStudioContextException();
                }

                var answerInput = (JObject)input.DeepClone();
                if (input["stage"] != null)
                {
                    answerInput["candidates"] = new JArray(((JArray)input["candidates"]).Reverse().Select(c => c.DeepClone()));
                }

                var answer = StageAnswer(answerInput);
                if (input["stage"] == null)
                {
                    answer["confidence"] = 0.96;
                    foreach (var row in answer["files"])
                    {
                        row["confidence"] = 0.96;
                    }
                }

                if (input["stage"] != null)
                {
                    compared = true;
                    if (uncertain)
                    {
                        answer["candidateId"] = null;
                        foreach (var row in answer["files"])
                        {
                            row["trackId"] = null;
                            row["status"] = "review";
                        }
                    }
                }

                return Response(answer);
            });
            Identify();
            Assert.That(compared, Is.True);
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Accepted), Is.EqualTo(!uncertain));
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.IsError), Is.False);
            if (!uncertain)
            {
                Assert.That(_local.AiMatch.CandidateId, Is.EqualTo("c1"));
                Assert.That(_local.AiMatch.Confidence, Is.EqualTo(0.96));
                Assert.That(_local.LocalTracks[0].AiMatch.TrackId, Is.EqualTo("c1t0"));
                Assert.That(_local.LocalTracks[0].AiMatch.Confidence, Is.EqualTo(0.96));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void identification_pipeline_should_route_to_ai_including_when_no_candidates_exist(bool noCandidates)
        {
            var candidateService = new Mock<ICandidateService>();
            var trackService = new Mock<ITrackService>();
            var config = new Mock<IConfigService>();
            var matcher = new Mock<ILmStudioMatcher>();
            var fingerprints = new Mock<IFingerprintingService>();
            var candidates = noCandidates ? new List<CandidateAlbumRelease>() : _candidates;
            config.SetupGet(c => c.AllowFingerprinting).Returns(AllowFingerprinting.Never);
            candidateService.Setup(c => c.GetDbCandidatesFromTags(It.IsAny<LocalAlbumRelease>(), It.IsAny<IdentificationOverrides>(), false)).Returns(candidates);
            trackService.Setup(t => t.GetTracksByReleases(It.IsAny<List<int>>())).Returns(new List<Track>());
            matcher.Setup(m => m.Identify(It.IsAny<LocalAlbumRelease>(), It.IsAny<List<CandidateAlbumRelease>>(), It.IsAny<List<LocalTrack>>()))
                .Callback<LocalAlbumRelease, List<CandidateAlbumRelease>, List<LocalTrack>>(_matcher.Identify);
            var service = new IdentificationService(
                trackService.Object,
                new Mock<ITrackGroupingService>().Object,
                fingerprints.Object,
                new Mock<IAudioTagService>().Object,
                new Mock<IAugmentingService>().Object,
                candidateService.Object,
                config.Object,
                matcher.Object,
                LogManager.GetLogger("test"));
            var releases = service.Identify(_local.LocalTracks, new IdentificationOverrides(), new ImportDecisionMakerConfig { SingleRelease = true });
            matcher.Verify(m => m.Identify(It.IsAny<LocalAlbumRelease>(), candidates, It.IsAny<List<LocalTrack>>()), Times.Once());
            fingerprints.Verify(f => f.Lookup(It.IsAny<List<LocalTrack>>(), It.IsAny<double>()), Times.Never());
            Assert.That(releases.Single().LocalTracks[0].AiMatch.Accepted, Is.EqualTo(!noCandidates));
        }

        [Test]
        public void large_candidate_set_should_be_bounded_and_evaluate_every_candidate()
        {
            var original = _candidates[0].AlbumRelease;
            _candidates = Enumerable.Range(0, 8).Select(index => new CandidateAlbumRelease(new AlbumRelease
            {
                Id = 20 + index, AlbumId = 10, Title = "Edition " + index, Album = original.Album,
                Tracks = Enumerable.Range(0, 20).Select(t => new Track
                {
                    Id = t + 1, AlbumReleaseId = 20 + index, Title = new string('x', 180) + t, Duration = 180000
                }).ToList()
            })).ToList();
            var evaluated = new HashSet<string>();
            var requests = 0;
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                requests++;
                Assert.That(payload.ToString(Formatting.None).Length, Is.LessThanOrEqualTo(settings.StageRequestCharacters));
                var input = Input(payload);
                foreach (var candidate in input["candidates"])
                {
                    evaluated.Add((string)candidate["candidateId"]);
                }

                return Response(StageAnswer(input));
            });
            Identify();
            Assert.That(requests, Is.GreaterThan(1));
            Assert.That(evaluated, Is.EquivalentTo(Enumerable.Range(0, 8).Select(i => "c" + i)));
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Accepted), Is.True);
        }

        [Test]
        public void uncertain_candidate_branch_must_not_be_silently_discarded()
        {
            var release = _candidates[0].AlbumRelease;
            _candidates.Add(new CandidateAlbumRelease(new AlbumRelease
            {
                Id = 21, AlbumId = 10, Title = "Other edition", Album = release.Album, Tracks = release.Tracks.Value
            }));
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns<JObject, LmStudioSettings>((payload, settings) =>
            {
                var input = Input(payload);
                if (((JArray)input["candidates"]).Count > 1)
                {
                    throw new LmStudioContextException();
                }

                var answer = StageAnswer(input);
                if ((string)input["candidates"][0]["candidateId"] == "c1")
                {
                    answer["candidateId"] = null;
                    foreach (var row in answer["files"])
                    {
                        row["status"] = "review";
                        row["trackId"] = null;
                    }
                }

                return Response(answer);
            });
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted && !f.AiMatch.IsError), Is.True);
        }

        [Test]
        public void should_use_ai_mapping_without_title_similarity_gate()
        {
            Identify();
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.Accepted), Is.True);
            Assert.That(_local.LocalTracks[0].Tracks.Single().Id, Is.EqualTo(1));
            Assert.That(new CloseTrackMatchSpecification(new Mock<IConfigService>().Object, LogManager.GetLogger("test")).IsSatisfiedBy(_local.LocalTracks[0], null).Accepted, Is.True);
            _audit.Verify(a => a.Write(_local), Times.Once());
        }

        [Test]
        public void review_mode_should_propose_mapping_but_reject_automatic_import()
        {
            _settings.Automatic = false;
            Identify();
            Assert.That(_local.LocalTracks[0].AiMatch.Proposed, Is.True);
            Assert.That(_local.LocalTracks[0].AiMatch.IsError, Is.False);
            Assert.That(_local.LocalTracks[0].AiMatch.Accepted, Is.False);
            Assert.That(_local.LocalTracks[0].Tracks.Single().Id, Is.EqualTo(1));
            Assert.That(new CloseTrackMatchSpecification(new Mock<IConfigService>().Object, LogManager.GetLogger("test")).IsSatisfiedBy(_local.LocalTracks[0], null).Accepted, Is.False);
        }

        [Test]
        public void uncertainty_should_leave_the_file_unmapped_and_block_an_incomplete_new_release()
        {
            _answer["files"][1]["trackId"] = null;
            _answer["files"][1]["status"] = "review";
            _answer["files"][1]["confidence"] = 0.5;
            Identify();
            Assert.That(_local.LocalTracks[0].AiMatch.Accepted, Is.True);
            Assert.That(_local.LocalTracks[1].AiMatch.Accepted, Is.False);
            Assert.That(_local.LocalTracks[1].AiMatch.IsError, Is.False);
            Assert.That(_local.LocalTracks[1].Tracks, Is.Empty);
            _local.NewDownload = true;
            Assert.That(new NoMissingOrUnmatchedTracksSpecification(LogManager.GetLogger("test")).IsSatisfiedBy(_local, null).Accepted, Is.False);
        }

        [TestCase("duplicate-track")]
        [TestCase("unknown-track")]
        [TestCase("duplicate-file")]
        [TestCase("missing-file")]
        [TestCase("unknown-release")]
        [TestCase("invalid-confidence")]
        [TestCase("missing-reason")]
        public void invalid_responses_should_invalidate_entire_batch(string corruption)
        {
            switch (corruption)
            {
                case "duplicate-track": _answer["files"][1]["trackId"] = "c0t0"; break;
                case "unknown-track": _answer["files"][1]["trackId"] = "c1t0"; break;
                case "duplicate-file": _answer["files"][1]["fileId"] = "f0"; break;
                case "missing-file": ((JArray)_answer["files"]).RemoveAt(1); break;
                case "unknown-release": _answer["candidateId"] = "c99"; break;
                case "invalid-confidence": _answer["files"][1]["confidence"] = 1.5; break;
                case "missing-reason": ((JObject)_answer["files"][1]).Remove("reason"); break;
            }

            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted && !f.Tracks.Any()), Is.True);
        }

        [Test]
        public void low_confidence_release_should_require_review()
        {
            _answer["confidence"] = 0.8;
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted), Is.True);
        }

        [Test]
        public void one_file_for_two_track_single_must_fail_completeness_even_with_confident_ai_match()
        {
            _local.LocalTracks.RemoveAt(1);
            ((JArray)_answer["files"]).RemoveAt(1);
            Identify();
            _local.NewDownload = true;
            var decision = new NoMissingOrUnmatchedTracksSpecification(LogManager.GetLogger("test")).IsSatisfiedBy(_local, null);
            Assert.That(_local.LocalTracks.Single().AiMatch.Accepted, Is.True);
            Assert.That(decision.Accepted, Is.False);
            Assert.That(decision.Reason, Does.Contain("1 of 2"));
            Assert.That(decision.Reason, Does.Contain("Second title"));
        }

        [Test]
        public void explicit_non_match_must_be_an_error_even_without_selected_release()
        {
            _answer["candidateId"] = null;
            foreach (var row in _answer["files"])
            {
                row["status"] = "no_match";
                row["trackId"] = null;
            }

            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.IsError && f.AiMatch.Status == "no_match"), Is.True);
        }

        [Test]
        public void timeout_should_clear_stale_match_and_require_review()
        {
            Identify();
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Throws(new TimeoutException());
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
            Assert.That(_local.LocalTracks.All(f => f.AiMatch.IsError), Is.True);
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted && !f.Tracks.Any()), Is.True);
        }

        [Test]
        public void truncated_output_should_never_be_imported()
        {
            _client.Setup(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>())).Returns(JObject.Parse("{'choices':[{'finish_reason':'length','message':{'content':'{}'}}]}"));
            Identify();
            Assert.That(_local.LocalTracks.All(f => !f.AiMatch.Accepted), Is.True);
        }

        [Test]
        public void oversized_batch_should_require_review_without_silently_truncating_candidates()
        {
            _settings.MaxRequestCharacters = 1000;
            Identify();
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Never());
            Assert.That(_local.LocalTracks[0].AiMatch.Reason, Does.Contain("size limit"));
        }

        [Test]
        public void no_candidates_should_require_review_without_http_call()
        {
            _candidates.Clear();
            Identify();
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Never());
            Assert.That(_local.LocalTracks[0].AiMatch.Reason, Does.Contain("No metadata candidates"));
        }

        [Test]
        public void invalid_configuration_should_require_review()
        {
            _settings.BaseUrl = "file:///tmp/model";
            Identify();
            _client.Verify(c => c.Complete(It.IsAny<JObject>(), It.IsAny<LmStudioSettings>()), Times.Never());
            Assert.That(_local.LocalTracks[0].AiMatch.Accepted, Is.False);
        }

        [Test]
        public void missing_track_confidence_should_not_default_to_zero()
        {
            ((JObject)_answer["files"][0]).Remove("confidence");
            Identify();
            Assert.That(_local.AlbumRelease, Is.Null);
        }
    }
}
