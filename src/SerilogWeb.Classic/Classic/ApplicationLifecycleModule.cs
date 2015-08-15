﻿// Copyright 2015 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Linq;
using System.Web;
using Serilog;
using Serilog.Events;
using SerilogWeb.Classic.Enrichers;
using System.Collections.Generic;
using System.Diagnostics;

namespace SerilogWeb.Classic
{
    /// <summary>
    /// HTTP module that logs application request and error events.
    /// </summary>
    public class ApplicationLifecycleModule : IHttpModule
    {
        private const String StopWatchKey = "stopWatchContextKey";

        static volatile LogPostedFormDataOption _logPostedFormData = LogPostedFormDataOption.Never;
        static volatile bool _isEnabled = true;
        static volatile bool _filterPasswordsInFormData = true;
        static volatile IEnumerable<String> _filteredKeywords = new[] { "password" };
        static volatile LogEventLevel _requestLoggingLevel = LogEventLevel.Information;
        static volatile LogEventLevel _formDataLoggingLevel = LogEventLevel.Debug;

        static volatile ILogger _logger;

        /// <summary>
        /// The globally-shared logger.
        /// 
        /// </summary>
        /// <exception cref="T:System.ArgumentNullException"/>
        public static ILogger Logger
        {
            get
            {
                return _logger ?? Log.Logger;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                _logger = value;
            }
        }

        /// <summary>
        /// Register the module with the application (called automatically;
        /// do not call this explicitly from your code).
        /// </summary>
        public static void Register()
        {
            HttpApplication.RegisterModule(typeof(ApplicationLifecycleModule));
        }

        /// <summary>
        /// When set to Always, form data will be written via an event (using
        /// severity from FormDataLoggingLevel).  When set to OnlyOnError, this
        /// will only be written if the Response has a 500 status.
        /// The default is Never. Requires that <see cref="IsEnabled"/> is also
        /// true (which it is, by default).
        /// </summary>
        public static LogPostedFormDataOption LogPostedFormData
        {
            get { return _logPostedFormData; }
            set { _logPostedFormData = value; }
        }

        /// <summary>
        /// When set to true (the default), any field containing password will 
        /// not have its value logged when DebugLogPostedFormData is enabled
        /// </summary>
        public static bool FilterPasswordsInFormData
        {
            get { return _filterPasswordsInFormData; }
            set { _filterPasswordsInFormData = value; }
        }

        /// <summary>
        /// When FilterPasswordsInFormData is true, any field containing keywords in this list will 
        /// not have its value logged when DebugLogPostedFormData is enabled
        /// </summary>
        public static IEnumerable<String> FilteredKeywordsInFormData
        {
            get { return _filteredKeywords; }
            set { _filteredKeywords = value; }
        }

        /// <summary>
        /// When set to true, request details and errors will be logged. The default
        /// is true.
        /// </summary>
        public static bool IsEnabled
        {
            get { return _isEnabled; }
            set { _isEnabled = value; }
        }

        /// <summary>
        /// The level at which to log HTTP requests. The default is Information.
        /// </summary>
        public static LogEventLevel RequestLoggingLevel
        {
            get { return _requestLoggingLevel; }
            set { _requestLoggingLevel = value; }
        }


        /// <summary>
        /// The level at which to log form values
        /// </summary>
        public static LogEventLevel FormDataLoggingLevel
        {
            get { return _formDataLoggingLevel; }
            set { _formDataLoggingLevel = value; }
        }

        /// <summary>
        /// Initializes a module and prepares it to handle requests.
        /// </summary>
        /// <param name="context">An <see cref="T:System.Web.HttpApplication"/> that provides access to the methods, properties, and events common to all application objects within an ASP.NET application </param>
        public void Init(HttpApplication context)
        {
            context.BeginRequest += (o, args) =>
            {
                context.Context.Items[StopWatchKey] = Stopwatch.StartNew();
            };

            context.EndRequest += (o, args) =>
            {
                Stopwatch stopwatch = (Stopwatch)context.Context.Items[StopWatchKey]; ;

                stopwatch.Stop();

                if (!_isEnabled) return;

                var request = HttpContextCurrent.Request;
                if (request == null)
                    return;

                Logger.Write(_requestLoggingLevel, "HTTP {Method} for {RawUrl} [{ElapsedMillseconds}ms]", request.HttpMethod, request.RawUrl, stopwatch.ElapsedMilliseconds);

                if (ShouldLogRequest())
                {
                    var form = request.Unvalidated.Form;

                    if (form.HasKeys())
                    {
                        var formData = form.AllKeys.SelectMany(k => (form.GetValues(k) ?? new string[0]).Select(v => new { Name = k, Value = FilterPasswords(k, v) }));
                        Logger.Write(_formDataLoggingLevel, "Client provided {@FormData}", formData);
                    }
                }
            };

            context.Error += Error;
        }

        static bool ShouldLogRequest()
        {
            return Logger.IsEnabled(_formDataLoggingLevel) 
                && (LogPostedFormData == LogPostedFormDataOption.Always
                || (LogPostedFormData == LogPostedFormDataOption.OnlyOnError && HttpContext.Current.Response.StatusCode >= 500));
        }

        /// <summary>
        /// Filters configured keywords from being logged
        /// </summary>
        /// <param name="key">Key of the pair</param>
        /// <param name="value">Value of the pair</param>
        static string FilterPasswords(string key, string value)
        {
            if (_filterPasswordsInFormData && _filteredKeywords.Any(keyword => key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) != -1))
            {
                return "********";
            }

            return value;
        }

        static void Error(object sender, EventArgs e)
        {
            if (!_isEnabled) return;

            var ex = ((HttpApplication)sender).Server.GetLastError();
            Logger.Error(ex, "Error caught in global handler: {ExceptionMessage}", ex.Message);
        }

        /// <summary>
        /// Disposes of the resources (other than memory) used by the module that implements <see cref="T:System.Web.IHttpModule"/>.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
