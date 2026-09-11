using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications.Search;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DecisionEngineTests.Search
{
    [TestFixture]
    public class SeasonMatchSpecificationFixture : TestBase<SeasonMatchSpecification>
    {
        private RemoteEpisode _remoteEpisode = new();
        private SeasonSearchCriteria _searchCriteria = new();
        private ReleaseDecisionInformation _information;

        [SetUp]
        public void Setup()
        {
            _remoteEpisode.ParsedEpisodeInfo = new ParsedEpisodeInfo
            {
                SeasonNumbers = new[] { 1, 2, 3, 4, 5 }
            };

            _searchCriteria.SeasonNumber = 3;
            _information = new ReleaseDecisionInformation(false, _searchCriteria);
        }

        [Test]
        public void should_return_true_if_searched_season_is_within_a_multi_season_pack()
        {
            Subject.IsSatisfiedBy(_remoteEpisode, _information).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_searched_season_is_outside_a_multi_season_pack()
        {
            _searchCriteria.SeasonNumber = 7;

            Subject.IsSatisfiedBy(_remoteEpisode, _information).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_searched_season_matches_a_single_season_release()
        {
            _remoteEpisode.ParsedEpisodeInfo.SeasonNumbers = new[] { 3 };

            Subject.IsSatisfiedBy(_remoteEpisode, _information).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_searched_season_does_not_match_a_single_season_release()
        {
            _remoteEpisode.ParsedEpisodeInfo.SeasonNumbers = new[] { 1 };

            Subject.IsSatisfiedBy(_remoteEpisode, _information).Accepted.Should().BeFalse();
        }
    }
}
