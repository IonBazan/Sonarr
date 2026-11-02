using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests.RssSync
{
    [TestFixture]
    public class IndexerTagSpecificationFixture : CoreTest<IndexerTagSpecification>
    {
        private IndexerTagSpecification _specification;

        private RemoteEpisode _parseResultMulti;
        private IndexerDefinition _fakeIndexerDefinition;
        private Series _fakeSeries;
        private Episode _firstEpisode;
        private Episode _secondEpisode;
        private ReleaseInfo _fakeRelease;

        [SetUp]
        public void Setup()
        {
            _fakeIndexerDefinition = new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int>()
            };

            _specification = Mocker.Resolve<IndexerTagSpecification>();

            _fakeSeries = Builder<Series>.CreateNew()
                .With(c => c.Monitored = true)
                .With(c => c.Tags = new HashSet<int>())
                .Build();

            _fakeRelease = new ReleaseInfo
            {
                IndexerId = 1
            };

            _firstEpisode = new Episode { Monitored = true };
            _secondEpisode = new Episode { Monitored = true };

            var doubleEpisodeList = new List<Episode> { _firstEpisode, _secondEpisode };

            _parseResultMulti = new RemoteEpisode
            {
                Series = _fakeSeries,
                Episodes = doubleEpisodeList,
                Release = _fakeRelease,
                Indexer = _fakeIndexerDefinition
            };
        }

        [Test]
        public void indexer_and_series_without_tags_should_return_true()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int>();
            _fakeSeries.Tags = new HashSet<int>();

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeTrue();
        }

        [Test]
        public void indexer_with_tags_series_without_tags_should_return_false()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int> { 123 };
            _fakeSeries.Tags = new HashSet<int>();

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeFalse();
        }

        [Test]
        public void indexer_without_tags_series_with_tags_should_return_true()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int>();
            _fakeSeries.Tags = new HashSet<int> { 123 };

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeTrue();
        }

        [Test]
        public void indexer_with_tags_series_with_matching_tags_should_return_true()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int> { 123, 456 };
            _fakeSeries.Tags = new HashSet<int> { 123, 789 };

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeTrue();
        }

        [Test]
        public void indexer_with_tags_series_with_different_tags_should_return_false()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int> { 456 };
            _fakeSeries.Tags = new HashSet<int> { 123, 789 };

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeFalse();
        }

        [Test]
        public void release_without_indexerid_should_return_true()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int> { 456 };
            _fakeSeries.Tags = new HashSet<int> { 123, 789 };
            _fakeRelease.IndexerId = 0;

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeTrue();
        }

        [Test]
        public void release_with_invalid_indexerid_should_return_true()
        {
            _fakeIndexerDefinition.Tags = new HashSet<int> { 456 };
            _fakeSeries.Tags = new HashSet<int> { 123, 789 };
            _fakeRelease.IndexerId = 2;
            _parseResultMulti.Indexer = null;

            _specification.IsSatisfiedBy(_parseResultMulti, new ReleaseDecisionInformation(false, new SingleEpisodeSearchCriteria { MonitoredEpisodesOnly = true })).Accepted.Should().BeTrue();
        }
    }
}
