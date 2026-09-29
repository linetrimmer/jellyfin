using System;
using System.Text;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers
{
    public class DynamicHlsControllerTests
    {
        [Theory]
        [InlineData(8, false, true, " -tag:v:0 hvc1 -strict -2")]
        [InlineData(8, false, false, " -tag:v:0 dvh1 -strict -2")]
        [InlineData(7, true, true, " -tag:v:0 hvc1 -strict -2")]
        [InlineData(7, true, false, " -tag:v:0 dvh1 -strict -2")]
        [InlineData(5, false, true, " -tag:v:0 dvh1 -strict -2")]
        [InlineData(7, false, true, " -tag:v:0 hvc1")]
        [InlineData(null, false, true, " -tag:v:0 hvc1")]
        public void GetVideoCodecTagArguments_PrefersHvc1OnlyForProfile81(
            int? profile,
            bool convertProfile7,
            bool preferDoviHvc1,
            string expected)
        {
            using var state = new StreamState(null!, TranscodingJobType.Hls, null!)
            {
                Request = new VideoRequestDto(),
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo(),
                OutputVideoCodec = "copy",
                VideoStream = new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Codec = "hevc",
                    ColorTransfer = "smpte2084",
                    DvProfile = profile,
                    DvBlSignalCompatibilityId = profile == 8 ? 1 : profile == 5 ? 0 : null,
                    RpuPresentFlag = profile.HasValue ? 1 : null,
                    BlPresentFlag = profile.HasValue ? 1 : null
                }
            };
            state.Request.StreamOptions["hevc-rangetype"] = "DOVI,DOVIWithHDR10,HDR10";
            if (convertProfile7)
            {
                state.Request.StreamOptions["doviP7ToP81"] = "true";
            }

            if (preferDoviHvc1)
            {
                state.Request.StreamOptions["preferDoviHvc1"] = "true";
            }

            Assert.Equal(expected, DynamicHlsController.GetVideoCodecTagArguments(state, "copy"));
        }

        [Theory]
        [InlineData(true, 7, "copy", " -tag:v:0 dvh1 -strict -2")]
        [InlineData(false, 7, "copy", " -tag:v:0 hvc1")]
        [InlineData(true, 8, "copy", " -tag:v:0 hvc1")]
        [InlineData(true, 7, "hevc", " -tag:v:0 hvc1")]
        public void GetVideoCodecTagArguments_OnlyTagsConvertedVideoAsDolbyVision(
            bool requested,
            int profile,
            string outputCodec,
            string expected)
        {
            using var state = new StreamState(null!, TranscodingJobType.Hls, null!)
            {
                Request = new VideoRequestDto(),
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo(),
                OutputVideoCodec = outputCodec,
                VideoStream = new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Codec = "hevc",
                    ColorTransfer = "smpte2084",
                    DvProfile = profile,
                    RpuPresentFlag = 1,
                    BlPresentFlag = 1
                }
            };
            if (requested)
            {
                state.Request.StreamOptions["doviP7ToP81"] = "true";
            }

            var selectedEncoder = outputCodec == "hevc" ? "libx265" : outputCodec;
            Assert.Equal(expected, DynamicHlsController.GetVideoCodecTagArguments(state, selectedEncoder));
        }

        [Theory]
        [InlineData(true, 7, true)]
        [InlineData(false, 7, false)]
        [InlineData(true, 8, true)]
        public void CanStreamCopyVideo_AllowsConvertedProfile7ForProfile8Range(bool requested, int profile, bool expected)
        {
            using var state = new StreamState(null!, TranscodingJobType.Hls, null!)
            {
                Request = new VideoRequestDto(),
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo(),
                SupportedVideoCodecs = ["hevc"],
                InputContainer = "mp4",
                VideoStream = new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Codec = "hevc",
                    ColorTransfer = "smpte2084",
                    DvProfile = profile,
                    DvBlSignalCompatibilityId = 1,
                    RpuPresentFlag = 1,
                    BlPresentFlag = 1
                }
            };
            state.Request.StreamOptions["hevc-rangetype"] = "DOVIWithHDR10";
            if (requested)
            {
                state.Request.StreamOptions["doviP7ToP81"] = "true";
            }

            var helper = new EncodingHelper(null!, null!, null!, null!, null!);
            Assert.Equal(expected, helper.CanStreamCopyVideo(state, state.VideoStream));
        }

        [Theory]
        [InlineData(true, 7, ",SUPPLEMENTAL-CODECS=\"dvh1.08.06/db1p\"")]
        [InlineData(false, 7, "")]
        [InlineData(false, 8, ",SUPPLEMENTAL-CODECS=\"dvh1.08.06/db1p\"")]
        public void AppendPlaylistSupplementalCodecsField_AdvertisesConvertedProfile8(
            bool requested,
            int profile,
            string expected)
        {
            using var state = new StreamState(null!, TranscodingJobType.Hls, null!)
            {
                Request = new VideoRequestDto(),
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo(),
                OutputVideoCodec = "copy",
                VideoStream = new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Codec = "hevc",
                    ColorTransfer = "smpte2084",
                    DvProfile = profile,
                    DvLevel = 6,
                    DvBlSignalCompatibilityId = 1,
                    RpuPresentFlag = 1,
                    BlPresentFlag = 1
                }
            };
            if (requested)
            {
                state.Request.StreamOptions["doviP7ToP81"] = "true";
            }

            var builder = new StringBuilder();
            DynamicHlsHelper.AppendPlaylistSupplementalCodecsField(builder, state);

            Assert.Equal(expected, builder.ToString());
        }

        [Theory]
        [InlineData("4", true, 7, 1, 1, "copy", "-bsf:v hevc_mp4toannexb,dovi_p7_to_p81")]
        [InlineData("0", true, 7, 1, 1, "copy", "-bsf:v dovi_p7_to_p81")]
        [InlineData("4", false, 7, 1, 1, "copy", "-bsf:v hevc_mp4toannexb")]
        [InlineData("4", true, 8, 1, 1, "copy", "-bsf:v hevc_mp4toannexb")]
        [InlineData("4", true, 7, 0, 1, "copy", "-bsf:v hevc_mp4toannexb")]
        [InlineData("4", true, 7, 1, 0, "copy", "-bsf:v hevc_mp4toannexb")]
        [InlineData("4", true, 7, 1, 1, "libx265", "-bsf:v hevc_mp4toannexb")]
        public void GetCopyVideoBitStreamArgs_RequiresCopyIntentAndProfile7Metadata(
            string nalLengthSize,
            bool requested,
            int profile,
            int rpuPresent,
            int blPresent,
            string outputCodec,
            string expected)
        {
            using var state = new StreamState(null!, TranscodingJobType.Hls, null!)
            {
                Request = new VideoRequestDto(),
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo(),
                OutputVideoCodec = outputCodec,
                VideoStream = new MediaStream
                {
                    Codec = "hevc",
                    NalLengthSize = nalLengthSize,
                    DvProfile = profile,
                    RpuPresentFlag = rpuPresent,
                    BlPresentFlag = blPresent
                }
            };
            if (requested)
            {
                state.Request.StreamOptions["doviP7ToP81"] = "true";
            }

            Assert.Equal(expected, DynamicHlsController.GetCopyVideoBitStreamArgs(state));
        }

        [Theory]
        [MemberData(nameof(GetSegmentLengths_Success_TestData))]
        public void GetSegmentLengths_Success(long runtimeTicks, int segmentlength, double[] expected)
        {
            var res = DynamicHlsController.GetSegmentLengthsInternal(runtimeTicks, segmentlength);
            Assert.Equal(expected.Length, res.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], res[i]);
            }
        }

        public static TheoryData<long, int, double[]> GetSegmentLengths_Success_TestData()
        {
            var data = new TheoryData<long, int, double[]>();
            data.Add(0, 6, Array.Empty<double>());
            data.Add(
                TimeSpan.FromSeconds(3).Ticks,
                6,
                new double[] { 3 });
            data.Add(
                TimeSpan.FromSeconds(6).Ticks,
                6,
                new double[] { 6 });
            data.Add(
                TimeSpan.FromSeconds(3.3333333).Ticks,
                6,
                new double[] { 3.3333333 });
            data.Add(
                TimeSpan.FromSeconds(9.3333333).Ticks,
                6,
                new double[] { 6, 3.3333333 });

            return data;
        }
    }
}
