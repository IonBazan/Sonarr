using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests.EpisodeRepositoryTests
{
    [TestFixture]
    public class EpisodesRepositoryReadFixture : DbTest<EpisodeRepository, Episode>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                        .With(s => s.Runtime = 30)
                                        .BuildNew();

            Db.Insert(_series);
        }

        [Test]
        public void should_get_episodes_by_file()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                .With(h => h.Quality = new QualityModel())
                .With(h => h.Languages = new List<Language> { Language.English })
                .BuildNew();

            Db.Insert(episodeFile);

            var episode = Builder<Episode>.CreateListOfSize(2)
                                        .All()
                                        .With(e => e.SeriesId = _series.Id)
                                        .With(e => e.EpisodeFileId = episodeFile.Id)
                                        .BuildListOfNew();

            Db.InsertMany(episode);

            var episodes = Subject.GetEpisodeByFileId(episodeFile.Id);
            episodes.Should().HaveCount(2);
        }

        [TestCase(1, -1, "TBA", true)]
        [TestCase(0, -1, "TBA", false)]
        [TestCase(1, 1, "TBA", false)]
        [TestCase(1, -1, "Pilot", false)]
        public void should_find_aired_episode_without_title(int seasonNumber, int airedDaysAgo, string title, bool expected)
        {
            var episode = Builder<Episode>.CreateNew()
                                          .With(e => e.SeriesId = _series.Id)
                                          .With(e => e.SeasonNumber = seasonNumber)
                                          .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(airedDaysAgo))
                                          .With(e => e.Title = title)
                                          .BuildNew();

            Db.Insert(episode);

            Subject.HasAiredEpisodeWithoutTitle(_series.Id).Should().Be(expected);
        }

        [TestCase(-7, true)]
        [TestCase(-100, false)]
        public void should_find_episode_aired_after_date(int airedDaysAgo, bool expected)
        {
            var episode = Builder<Episode>.CreateNew()
                                          .With(e => e.SeriesId = _series.Id)
                                          .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(airedDaysAgo))
                                          .BuildNew();

            Db.Insert(episode);

            Subject.HasEpisodeAiredAfter(_series.Id, DateTime.UtcNow.AddDays(-30)).Should().Be(expected);
        }
    }
}
