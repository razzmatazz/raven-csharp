﻿#region License

// Copyright (c) 2014 The Sentry Team and individual contributors.
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without modification, are permitted
// provided that the following conditions are met:
// 
//     1. Redistributions of source code must retain the above copyright notice, this list of
//        conditions and the following disclaimer.
// 
//     2. Redistributions in binary form must reproduce the above copyright notice, this list of
//        conditions and the following disclaimer in the documentation and/or other materials
//        provided with the distribution.
// 
//     3. Neither the name of the Sentry nor the names of its contributors may be used to
//        endorse or promote products derived from this software without specific prior written
//        permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
// IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
// WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
// ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

#if !(net40)

using System.IO;
using System.Net;

using Newtonsoft.Json;

using SharpRaven.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using SharpRaven.Data;

namespace SharpRaven
{
    /// <summary>
    /// The Raven Client, responsible for capturing exceptions and sending them to Sentry.
    /// </summary>
    public partial class RavenClient
    {
        /// <summary>
        /// Captures the <see cref="Exception" />.
        /// </summary>
        /// <param name="exception">The <see cref="Exception" /> to capture.</param>
        /// <param name="message">The optional message to capture. Default: <see cref="Exception.Message" />.</param>
        /// <param name="level">The <see cref="ErrorLevel" /> of the captured <paramref name="exception" />. Default: <see cref="ErrorLevel.Error"/>.</param>
        /// <param name="tags">The tags to annotate the captured <paramref name="exception" /> with.</param>
        /// <param name="fingerprint">The custom fingerprint to annotate the captured <paramref name="message" /> with.</param>
        /// <param name="extra">The extra metadata to send with the captured <paramref name="exception" />.</param>
        /// <returns>
        /// The <see cref="JsonPacket.EventID" /> of the successfully captured <paramref name="exception" />, or <c>null</c> if it fails.
        /// </returns>
        public async Task<string> CaptureExceptionAsync(Exception exception,
                                                        SentryMessage message = null,
                                                        ErrorLevel level = ErrorLevel.Error,
                                                        IDictionary<string, string> tags = null,
                                                        string[] fingerprint = null,
                                                        object extra = null)
        {
            JsonPacket packet = this.jsonPacketFactory.Create(CurrentDsn.ProjectID,
                                                              exception,
                                                              message,
                                                              level,
                                                              tags,
                                                              fingerprint,
                                                              extra);

            return await SendAsync(packet);
        }


        /// <summary>
        /// Captures the message.
        /// </summary>
        /// <param name="message">The message to capture.</param>
        /// <param name="level">The <see cref="ErrorLevel" /> of the captured <paramref name="message"/>. Default <see cref="ErrorLevel.Info"/>.</param>
        /// <param name="tags">The tags to annotate the captured <paramref name="message"/> with.</param>
        /// <param name="fingerprint">The custom fingerprint to annotate the captured <paramref name="message" /> with.</param>
        /// <param name="extra">The extra metadata to send with the captured <paramref name="message"/>.</param>
        /// <returns>
        /// The <see cref="JsonPacket.EventID"/> of the successfully captured <paramref name="message"/>, or <c>null</c> if it fails.
        /// </returns>
        public async Task<string> CaptureMessageAsync(SentryMessage message,
                                                      ErrorLevel level = ErrorLevel.Info,
                                                      IDictionary<string, string> tags = null,
                                                      string[] fingerprint = null,
                                                      object extra = null)
        {
            JsonPacket packet = this.jsonPacketFactory.Create(CurrentDsn.ProjectID, message, level, tags, fingerprint, extra);

            return await SendAsync(packet);
        }


        /// <summary>
        /// Sends the specified packet to Sentry.
        /// </summary>
        /// <param name="packet">The packet to send.</param>
        /// <param name="dsn">The Data Source Name in Sentry.</param>
        /// <returns>
        /// The <see cref="JsonPacket.EventID"/> of the successfully captured JSON packet, or <c>null</c> if it fails.
        /// </returns>
        protected virtual async Task<string> SendAsync(JsonPacket packet)
        {
            try
            {
                packet = PreparePacket(packet);

                var request = (HttpWebRequest)WebRequest.Create(CurrentDsn.SentryUri);
                request.Timeout = (int)Timeout.TotalMilliseconds;
                request.ReadWriteTimeout = (int)Timeout.TotalMilliseconds;
                request.Method = "POST";
                request.Accept = "application/json";
                request.Headers.Add("X-Sentry-Auth", PacketBuilder.CreateAuthenticationHeader(CurrentDsn));
                request.UserAgent = PacketBuilder.UserAgent;

                if (Compression)
                {
                    request.Headers.Add(HttpRequestHeader.ContentEncoding, "gzip");
                    request.AutomaticDecompression = DecompressionMethods.Deflate;
                    request.ContentType = "application/octet-stream";
                }
                else
                    request.ContentType = "application/json; charset=utf-8";

                /*string data = packet.ToString(Formatting.Indented);
            Console.WriteLine(data);*/

                string data = packet.ToString(Formatting.None);

                if (LogScrubber != null)
                    data = LogScrubber.Scrub(data);

                // Write the messagebody.
                using (Stream s = await request.GetRequestStreamAsync())
                {
                    if (Compression)
                        await GzipUtil.WriteAsync(data, s);
                    else
                    {
                        using (StreamWriter sw = new StreamWriter(s))
                        {
                            await sw.WriteAsync(data);
                        }
                    }
                }

                using (HttpWebResponse wr = (HttpWebResponse)(await request.GetResponseAsync()))
                {
                    using (Stream responseStream = wr.GetResponseStream())
                    {
                        if (responseStream == null)
                            return null;

                        using (StreamReader sr = new StreamReader(responseStream))
                        {
                            string content = await sr.ReadToEndAsync();
                            var response = JsonConvert.DeserializeObject<dynamic>(content);
                            return response.id;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
    }
}

#endif