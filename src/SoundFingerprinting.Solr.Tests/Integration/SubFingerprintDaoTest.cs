﻿namespace SoundFingerprinting.Solr.Tests.Integration
{
    using System.Collections.Generic;
    using System.Linq;

    using Audio;
    using Audio.NAudio;

    using NUnit.Framework;

    using SoundFingerprinting.Builder;
    using SoundFingerprinting.DAO;
    using SoundFingerprinting.DAO.Data;
    using SoundFingerprinting.Data;

    [TestFixture]
    public class SubFingerprintDaoTest : IntegrationTestWithSampleFiles
    {
        private readonly FingerprintCommandBuilder fingerprintCommandBuilder = new FingerprintCommandBuilder();
        private readonly IAudioService audioService = new NAudioService();
        private readonly SubFingerprintDao subFingerprintDao = new SubFingerprintDao();
        private readonly TrackDao trackDao = new TrackDao();

        [TearDown]
        public void TearDown()
        {
            var allTracks = trackDao.ReadAll();
            foreach (var track in allTracks)
            {
                trackDao.DeleteTrack(track.TrackReference);
            }
        }

        [Test]
        public void ShouldInsertAndReadSubFingerprints()
        {
            var track = new TrackData("isrc", "artist", "title", "album", 1986, 200);
            var trackReference = trackDao.InsertTrack(track);
            const int NumberOfHashBins = 100;
            var genericHashBuckets = new long[25];
            var hashedFingerprints =
                Enumerable.Range(0, NumberOfHashBins)
                    .Select(
                        sequenceNumber =>
                            new HashedFingerprint(
                                GenericSignature(),
                                genericHashBuckets,
                                sequenceNumber,
                                sequenceNumber * 0.928,
                                Enumerable.Empty<string>()));

            InsertHashedFingerprintsForTrack(hashedFingerprints, trackReference);

            var hashedFingerprintss = subFingerprintDao.ReadHashedFingerprintsByTrackReference(track.TrackReference);
            Assert.AreEqual(NumberOfHashBins, hashedFingerprintss.Count);
            foreach (var hashedFingerprint in hashedFingerprintss)
            {
                CollectionAssert.AreEqual(genericHashBuckets, hashedFingerprint.HashBins);
            }
        }

        [Test]
        public void SameNumberOfHashBinsIsInsertedInAllTablesWhenFingerprintingEntireSongTest()
        {
            var track = new TrackData(string.Empty, "artist", "title", "album", 1986, 197d);
            var trackReference = trackDao.InsertTrack(track);
            var hashedFingerprints = fingerprintCommandBuilder
                .BuildFingerprintCommand()
                .From(GetAudioSamples())
                .UsingServices(audioService)
                .Hash()
                .Result;

            InsertHashedFingerprintsForTrack(hashedFingerprints, trackReference);

            var hashes = subFingerprintDao.ReadHashedFingerprintsByTrackReference(track.TrackReference);
            Assert.AreEqual(hashedFingerprints.Count, hashes.Count);
            foreach (var data in hashes)
            {
                Assert.AreEqual(25, data.HashBins.Length);
            }
        }

        [Test]
        public void ReadByTrackGroupIdWorksAsExpectedTest()
        {
            var firstTrack = new TrackData(string.Empty, "first-artist", "title", "album", 1986, 197d);
            var secondTrack = new TrackData(string.Empty, "second-artist", "title", "album", 1986, 197d);

            var firstTrackReference = trackDao.InsertTrack(firstTrack);
            var secondTrackReference = trackDao.InsertTrack(secondTrack);

            var hashedFingerprintsForFirstTrack = fingerprintCommandBuilder
                .BuildFingerprintCommand()
                .From(GetAudioSamples())
                .WithFingerprintConfig(config =>
                {
                    config.Clusters = new[] { "first-group-id" };
                }).UsingServices(audioService)
                .Hash()
                .Result;

            InsertHashedFingerprintsForTrack(hashedFingerprintsForFirstTrack, firstTrackReference);

            var hashedFingerprintsForSecondTrack = fingerprintCommandBuilder
               .BuildFingerprintCommand()
               .From(GetAudioSamples())
               .WithFingerprintConfig(config =>
               {
                   config.Clusters = new[] { "second-group-id" };
               }).UsingServices(audioService)
               .Hash()
               .Result;
            InsertHashedFingerprintsForTrack(hashedFingerprintsForSecondTrack, secondTrackReference);

            const int ThresholdVotes = 25;
            foreach (var hashedFingerprint in hashedFingerprintsForFirstTrack)
            {
                var subFingerprintData = subFingerprintDao.ReadSubFingerprints(
                        hashedFingerprint.HashBins, ThresholdVotes, new[] { "first-group-id" }).ToList();

                Assert.AreEqual(1, subFingerprintData.Count);
                Assert.AreEqual(firstTrackReference, subFingerprintData[0].TrackReference);

                subFingerprintData = subFingerprintDao.ReadSubFingerprints(
                        hashedFingerprint.HashBins, ThresholdVotes, new[] { "second-group-id" }).ToList();

                Assert.AreEqual(1, subFingerprintData.Count);
                Assert.AreEqual(secondTrackReference, subFingerprintData[0].TrackReference);

                subFingerprintData = subFingerprintDao.ReadSubFingerprints(hashedFingerprint.HashBins, ThresholdVotes, Enumerable.Empty<string>()).ToList();
                Assert.AreEqual(2, subFingerprintData.Count);
            }
        }

        [Test]
        public void ReadHashDataByTrackTest()
        {
            TrackData firstTrack = new TrackData("isrc", "artist", "title", "album", 2012, 200);

            var firstTrackReference = trackDao.InsertTrack(firstTrack);

            var firstHashData = fingerprintCommandBuilder
                .BuildFingerprintCommand()
                .From(GetAudioSamples())
                .UsingServices(audioService)
                .Hash()
                .Result;

            InsertHashedFingerprintsForTrack(firstHashData, firstTrackReference);

            TrackData secondTrack = new TrackData("isrc", "artist", "title", "album", 2012, 200);

            var secondTrackReference = trackDao.InsertTrack(secondTrack);

            var secondHashData = fingerprintCommandBuilder
                .BuildFingerprintCommand()
                .From(GetAudioSamples())
                .UsingServices(audioService)
                .Hash()
                .Result;

            InsertHashedFingerprintsForTrack(secondHashData, secondTrackReference);

            var resultFirstHashData = subFingerprintDao.ReadHashedFingerprintsByTrackReference(firstTrackReference);
            AssertHashDatasAreTheSame(firstHashData, resultFirstHashData);

            IList<HashedFingerprint> resultSecondHashData = subFingerprintDao.ReadHashedFingerprintsByTrackReference(secondTrackReference);
            AssertHashDatasAreTheSame(secondHashData, resultSecondHashData);
        }

        private void InsertHashedFingerprintsForTrack(IEnumerable<HashedFingerprint> hashedFingerprints, IModelReference trackReference)
        {
            subFingerprintDao.InsertHashDataForTrack(hashedFingerprints, trackReference);
        }
    }
}