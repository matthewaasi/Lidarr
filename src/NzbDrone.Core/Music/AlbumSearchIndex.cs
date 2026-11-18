using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Music
{
    internal sealed class AlbumSearchIndex
    {
        private readonly ILookup<string, Album> _cleanTitles;
        private readonly ILookup<string, Album> _titles;

        public AlbumSearchIndex(List<Album> albums)
        {
            _cleanTitles = albums.ToLookup(x => x.CleanTitle, StringComparer.Ordinal);
            _titles = albums.ToLookup(x => x.Title, StringComparer.Ordinal);
        }

        public Album Find(string title)
        {
            var cleanTitle = title.CleanArtistName();
            if (string.IsNullOrEmpty(cleanTitle))
            {
                cleanTitle = title;
            }

            return _cleanTitles[cleanTitle].Union(_titles[title], (IEqualityComparer<Album>)ReferenceEqualityComparer.Instance).ExclusiveOrDefault();
        }
    }
}
