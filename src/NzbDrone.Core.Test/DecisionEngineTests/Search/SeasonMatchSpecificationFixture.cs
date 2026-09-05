using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Specifications.Search;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Search
{
    [TestFixture]
    public class SeasonMatchSpecificationFixture : CoreTest<SeasonMatchSpecification>
    {
        private RemoteEpisode _remoteEpisode;

        [SetUp]
        public void Setup()
        {
            _remoteEpisode = new RemoteEpisode
            {
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    SeasonNumbers = new[] { 1 }
                }
            };
        }

        [Test]
        public void should_return_true_if_season_matches_search()
        {
            Subject.IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, new SeasonSearchCriteria { SeasonNumber = 1 }))
                   .Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_season_does_not_match_search()
        {
            Subject.IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, new SeasonSearchCriteria { SeasonNumber = 2 }))
                   .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_searched_season_is_within_multi_season_pack_range()
        {
            _remoteEpisode.ParsedEpisodeInfo.SeasonNumbers = new[] { 1, 2, 3, 4, 5 };

            Subject.IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, new SeasonSearchCriteria { SeasonNumber = 3 }))
                   .Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_searched_season_is_outside_multi_season_pack_range()
        {
            _remoteEpisode.ParsedEpisodeInfo.SeasonNumbers = new[] { 1, 2, 3, 4, 5 };

            Subject.IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, new SeasonSearchCriteria { SeasonNumber = 7 }))
                   .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_search_criteria_is_null()
        {
            Subject.IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, null))
                   .Accepted.Should().BeTrue();
        }
    }
}
