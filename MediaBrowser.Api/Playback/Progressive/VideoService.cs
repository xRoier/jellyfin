﻿using System.IO;
using MediaBrowser.Common.IO;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using System;
using MediaBrowser.Controller.Library;
using ServiceStack.ServiceHost;

namespace MediaBrowser.Api.Playback.Progressive
{
    /// <summary>
    /// Class GetAudioStream
    /// </summary>
    [Route("/Videos/{Id}/stream.ts", "GET")]
    [Route("/Videos/{Id}/stream.webm", "GET")]
    [Route("/Videos/{Id}/stream.asf", "GET")]
    [Route("/Videos/{Id}/stream.wmv", "GET")]
    [Route("/Videos/{Id}/stream.ogv", "GET")]
    [Route("/Videos/{Id}/stream.mp4", "GET")]
    [Route("/Videos/{Id}/stream.m4v", "GET")]
    [Route("/Videos/{Id}/stream.mkv", "GET")]
    [Route("/Videos/{Id}/stream.mpeg", "GET")]
    [Route("/Videos/{Id}/stream.avi", "GET")]
    [Route("/Videos/{Id}/stream.m2ts", "GET")]
    [Route("/Videos/{Id}/stream", "GET")]
    [Route("/Videos/{Id}/stream.ts", "HEAD")]
    [Route("/Videos/{Id}/stream.webm", "HEAD")]
    [Route("/Videos/{Id}/stream.asf", "HEAD")]
    [Route("/Videos/{Id}/stream.wmv", "HEAD")]
    [Route("/Videos/{Id}/stream.ogv", "HEAD")]
    [Route("/Videos/{Id}/stream.mp4", "HEAD")]
    [Route("/Videos/{Id}/stream.m4v", "HEAD")]
    [Route("/Videos/{Id}/stream.mkv", "HEAD")]
    [Route("/Videos/{Id}/stream.mpeg", "HEAD")]
    [Route("/Videos/{Id}/stream.avi", "HEAD")]
    [Route("/Videos/{Id}/stream.m2ts", "HEAD")]
    [Route("/Videos/{Id}/stream", "HEAD")]
    [ServiceStack.ServiceHost.Api(Description = "Gets a video stream")]
    public class GetVideoStream : VideoStreamRequest
    {

    }

    /// <summary>
    /// Class VideoService
    /// </summary>
    public class VideoService : BaseProgressiveStreamingService
    {
        public VideoService(IServerApplicationPaths appPaths, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager)
            : base(appPaths, userManager, libraryManager, isoManager)
        {
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetVideoStream request)
        {
            return ProcessRequest(request, false);
        }

        public object Head(GetVideoStream request)
        {
            return ProcessRequest(request, true);
        }

        /// <summary>
        /// Gets the command line arguments.
        /// </summary>
        /// <param name="outputPath">The output path.</param>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        protected override string GetCommandLineArguments(string outputPath, StreamState state)
        {
            var video = (Video)state.Item;

            var probeSize = Kernel.Instance.FFMpegManager.GetProbeSizeArgument(video.VideoType, video.IsoType);

            // Get the output codec name
            var videoCodec = GetVideoCodec(state.VideoRequest);

            var graphicalSubtitleParam = string.Empty;

            if (state.SubtitleStream != null)
            {
                // This is for internal graphical subs
                if (!state.SubtitleStream.IsExternal && (state.SubtitleStream.Codec.IndexOf("pgs", StringComparison.OrdinalIgnoreCase) != -1 || state.SubtitleStream.Codec.IndexOf("dvd", StringComparison.OrdinalIgnoreCase) != -1))
                {
                    graphicalSubtitleParam = GetInternalGraphicalSubtitleParam(state, videoCodec);
                }
            }

            var format = string.Empty;
            var keyFrame = string.Empty;

            if (string.Equals(Path.GetExtension(outputPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                format = " -f mp4 -movflags frag_keyframe+empty_moov";
                var framerate = state.VideoRequest.Framerate ??
                                state.VideoStream.AverageFrameRate ?? state.VideoStream.RealFrameRate ?? 23.976;

                framerate *= 2;

                keyFrame = " -g " + Math.Round(framerate);
            }

            return string.Format("{0} {1} -i {2}{3}{4} -threads 0 {5} {6}{7} {8}{9} \"{10}\"",
                probeSize,
                GetFastSeekCommandLineParameter(state.Request),
                GetInputArgument(video, state.IsoMount),
                GetSlowSeekCommandLineParameter(state.Request),
                keyFrame,
                GetMapArgs(state),
                GetVideoArguments(state, videoCodec),
                graphicalSubtitleParam,
                GetAudioArguments(state),
                format,
                outputPath
                ).Trim();
        }

        /// <summary>
        /// Gets video arguments to pass to ffmpeg
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="videoCodec">The video codec.</param>
        /// <returns>System.String.</returns>
        private string GetVideoArguments(StreamState state, string videoCodec)
        {
            var args = "-vcodec " + videoCodec;

            // See if we can save come cpu cycles by avoiding encoding
            if (videoCodec.Equals("copy", StringComparison.OrdinalIgnoreCase))
            {
                return IsH264(state.VideoStream) ? args + " -bsf h264_mp4toannexb" : args;
            }

            const string keyFrameArg = " -force_key_frames expr:if(isnan(prev_forced_t),gte(t,0),gte(t,prev_forced_t+2))";

            args += keyFrameArg;

            var request = state.VideoRequest;

            // Add resolution params, if specified
            if (request.Width.HasValue || request.Height.HasValue || request.MaxHeight.HasValue || request.MaxWidth.HasValue)
            {
                args += GetOutputSizeParam(state, videoCodec);
            }

            if (request.Framerate.HasValue)
            {
                args += string.Format(" -r {0}", request.Framerate.Value);
            }

            // Add the audio bitrate
            var qualityParam = GetVideoQualityParam(request, videoCodec);

            if (!string.IsNullOrEmpty(qualityParam))
            {
                args += " " + qualityParam;
            }

            args += " -vsync vfr";

            if (!string.IsNullOrEmpty(state.VideoRequest.Profile))
            {
                args += " -profile:v " + state.VideoRequest.Profile;
            }

            if (!string.IsNullOrEmpty(state.VideoRequest.Level))
            {
                args += " -level 3 " + state.VideoRequest.Level;
            }

            return args;
        }

        /// <summary>
        /// Gets audio arguments to pass to ffmpeg
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        private string GetAudioArguments(StreamState state)
        {
            // If the video doesn't have an audio stream, return a default.
            if (state.AudioStream == null)
            {
                return string.Empty;
            }

            var request = state.Request;

            // Get the output codec name
            var codec = GetAudioCodec(request);

            if (codec.Equals("copy", StringComparison.OrdinalIgnoreCase))
            {
                return "-acodec copy";
            }
            
            var args = "-acodec " + codec;

            // Add the number of audio channels
            var channels = GetNumAudioChannelsParam(request, state.AudioStream);

            if (channels.HasValue)
            {
                args += " -ac " + channels.Value;
            }

            if (request.AudioSampleRate.HasValue)
            {
                args += " -ar " + request.AudioSampleRate.Value;
            }

            if (request.AudioBitRate.HasValue)
            {
                args += " -ab " + request.AudioBitRate.Value;
            }

            var volParam = string.Empty;

            // Boost volume to 200% when downsampling from 6ch to 2ch
            if (channels.HasValue && channels.Value <= 2 && state.AudioStream.Channels.HasValue && state.AudioStream.Channels.Value > 5)
            {
                volParam = ",volume=2.000000";
            }

            args += string.Format(" -af \"aresample=async=1000{0}\"", volParam);

            return args;
        }

        /// <summary>
        /// Gets the video bitrate to specify on the command line
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="videoCodec">The video codec.</param>
        /// <returns>System.String.</returns>
        private string GetVideoQualityParam(VideoStreamRequest request, string videoCodec)
        {
            var args = string.Empty;

            // webm
            if (videoCodec.Equals("libvpx", StringComparison.OrdinalIgnoreCase))
            {
                args = "-quality realtime -profile:v 1 -slices 4";
            }

            // asf/wmv
            else if (videoCodec.Equals("wmv2", StringComparison.OrdinalIgnoreCase))
            {
                args = "-g 100 -qmax 15";
            }

            else if (videoCodec.Equals("libx264", StringComparison.OrdinalIgnoreCase))
            {
                args = "-preset superfast";
            }

            if (request.VideoBitRate.HasValue)
            {
                args += " -b:v " + request.VideoBitRate;
            }

            return args.Trim();
        }
    }
}
