﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Extensions.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SendGrid.Helpers.Mail;

namespace Microsoft.Azure.WebJobs.Extensions.SendGrid
{
    /// <summary>
    /// Defines the configuration options for the SendGrid binding.
    /// </summary>
    public class SendGridOptions
    {
        /// <summary>
        /// Gets or sets the SendGrid ApiKey. If not explicitly set, the value will be defaulted
        /// to the value specified via the 'AzureWebJobsSendGridApiKey' app setting or the
        /// 'AzureWebJobsSendGridApiKey' environment variable.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the default "to" address that will be used for messages.
        /// This value can be overridden by job functions.
        /// </summary>
        /// <remarks>
        /// An example of when it would be useful to provide a default value for 'to' 
        /// would be for emailing your own admin account to notify you when particular
        /// jobs are executed. In this case, job functions can specify minimal info in
        /// their bindings, for example just a Subject and Text body.
        /// </remarks>
        public EmailAddress ToAddress { get; set; }

        /// <summary>
        /// Gets or sets the default "from" address that will be used for messages.
        /// This value can be overridden by job functions.
        /// </summary>
        public EmailAddress FromAddress { get; set; }

        internal class Setup : IConfigureOptions<SendGridOptions>
        {
            private readonly IConfiguration _config;

            public Setup(IConfiguration config)
            {
                _config = config;
            }

            public void Configure(SendGridOptions options)
            {
                // TODO: Need to access raw to/from strings from config
                // and create EmailAddresses from them
                string to = null;
                string from = null;
                options.ToAddress = SendGridHelpers.Apply(options.ToAddress, to);
                options.FromAddress = SendGridHelpers.Apply(options.FromAddress, from);
            }
        }
    }
}
