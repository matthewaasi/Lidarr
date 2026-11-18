using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Music
{
    internal sealed class ArtistSearchIndex
    {
        private readonly List<Artist> _artists;
        private readonly ILookup<string, Artist> _exact;
        private readonly Dictionary<Artist, Lazy<string[]>> _aliases;

        public ArtistSearchIndex(List<Artist> artists)
        {
            _artists = artists;
            _exact = artists.ToLookup(x => x.CleanName, StringComparer.Ordinal);
            _aliases = artists.ToDictionary(
                x => x,
                x => new Lazy<string[]>(() => x.Metadata.Value.Aliases.Concat(new[] { x.Name }).Select(name => name.CleanArtistName()).ToArray()),
                (IEqualityComparer<Artist>)ReferenceEqualityComparer.Instance);
        }

        public Artist FindExact(string cleanName)
        {
            var matches = _exact[cleanName].ToList();
            if (matches.Count > 1)
            {
                throw new MultipleArtistsFoundException(matches, "Expected one artist, but found {0}. Matching artists: {1}", matches.Count, string.Join(", ", matches));
            }

            return matches.SingleOrDefault();
        }

        public Artist FindInexact(string title)
        {
            var cleanTitle = title.CleanArtistName();
            var result = Unique(a => a.CleanName.FuzzyMatch(cleanTitle));
            if (result != null)
            {
                return result;
            }

            result = Unique(a => a.Name.FuzzyMatch(title));
            if (result != null)
            {
                return result;
            }

            result = Unique(a => _aliases[a].Value.Max(x => x.FuzzyMatch(cleanTitle)));
            if (result != null)
            {
                return result;
            }

            var alternate = title.StartsWith("The ", StringComparison.CurrentCultureIgnoreCase)
                ? title.Substring(4).CleanArtistName()
                : "the" + cleanTitle;
            return Unique(a => a.CleanName.FuzzyMatch(alternate));
        }

        private Artist Unique(Func<Artist, double> score)
        {
            Artist best = null;
            var highest = double.NegativeInfinity;
            var second = double.NegativeInfinity;
            foreach (var artist in _artists)
            {
                var value = score(artist);
                if (value > highest)
                {
                    second = highest;
                    highest = value;
                    best = artist;
                }
                else if (value > second)
                {
                    second = value;
                }
            }

            // Equivalent to the legacy sorted chain having exactly one result.
            return highest > 0.8 && highest - second >= 0.2 ? best : null;
        }
    }
}
