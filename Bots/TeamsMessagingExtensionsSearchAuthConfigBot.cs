// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.BotBuilderSamples.Bots
{
    public class TeamsMessagingExtensionsSearchAuthConfigBot : TeamsActivityHandler
    {
        readonly string _connectionName;

        public TeamsMessagingExtensionsSearchAuthConfigBot(IConfiguration configuration, UserState userState)
        {
            _connectionName = configuration["ConnectionName"] ?? throw new NullReferenceException("ConnectionName");
        }

        protected override Task<MessagingExtensionActionResponse> OnTeamsMessagingExtensionSubmitActionAsync(ITurnContext<IInvokeActivity> turnContext, MessagingExtensionAction action, CancellationToken cancellationToken)
        {
            // This method is to handle the 'Close' button on the confirmation Task Module after the user signs out.
            return Task.FromResult(new MessagingExtensionActionResponse());
        }

        /// <summary>
        /// Safely casts an object to an object of type <typeparamref name="T"/> .
        /// </summary>
        /// <param name="value">The object to be casted.</param>
        /// <returns>The object casted in the new type.</returns>
        private static T SafeCast<T>(object value)
        {
            var obj = value as JObject;
            if (obj == null)
            {
                throw new InvokeResponseException(HttpStatusCode.BadRequest, $"expected type '{value.GetType().Name}'");
            }

            return obj.ToObject<T>();
        }

        protected override async Task<MessagingExtensionActionResponse> OnTeamsMessagingExtensionFetchTaskAsync(ITurnContext<IInvokeActivity> turnContext, MessagingExtensionAction action, CancellationToken cancellationToken)
        {
            if (action.CommandId.ToUpper() == "COMPOSEEMAIL")
            {
                var attachments = new List<MessagingExtensionAttachment>();
                // When the Bot Service Auth flow completes, the action.State will contain a magic code used for verification.
                var magicCode = string.Empty;

                var actionData = SafeCast<MessagingExtensionActionWithState>(turnContext.Activity.Value); // This is custom class created to get state data.
                var state = actionData.State; // Check the state value 

                if (!string.IsNullOrEmpty(state))
                {
                    var parsed = 0;
                    if (int.TryParse(state, out parsed))
                    {
                        magicCode = parsed.ToString();
                    }
                }

                var tokenResponse = await (turnContext.Adapter as IUserTokenProvider).GetUserTokenAsync(turnContext, _connectionName, magicCode, cancellationToken: cancellationToken);
                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.Token))
                {
                    // There is no token, so the user has not signed in yet.

                    // Retrieve the OAuth Sign in Link to use in the MessagingExtensionResult Suggested Actions
                    var signInLink = await (turnContext.Adapter as IUserTokenProvider).GetOauthSignInLinkAsync(turnContext, _connectionName, cancellationToken);

                    return new MessagingExtensionActionResponse
                    {
                        ComposeExtension = new MessagingExtensionResult
                        {
                            Type = "auth",
                            SuggestedActions = new MessagingExtensionSuggestedAction
                            {
                                Actions = new List<CardAction>
                                {
                                    new CardAction
                                    {
                                        Type = ActionTypes.OpenUrl,
                                        Value = signInLink,
                                        Title = "Bot Service OAuth",
                                    },
                                },
                            },
                        },
                    };
                }

                var client = new SimpleGraphClient(tokenResponse.Token);

                var profile = await client.GetMyProfile();

                return new MessagingExtensionActionResponse
                {
                    Task = new TaskModuleContinueResponse
                    {
                        Value = new TaskModuleTaskInfo
                        {
                            Card = GetProfileCard(profile),
                            Height = 250,
                            Width = 400,
                            Title = "Adaptive Card: Inputs",
                        },
                    },
                };
            }

            if (action.CommandId.ToUpper() == "SIGNOUTCOMMAND")
            {
                await (turnContext.Adapter as IUserTokenProvider).SignOutUserAsync(turnContext, _connectionName, turnContext.Activity.From.Id, cancellationToken);

                return new MessagingExtensionActionResponse
                {
                    Task = new TaskModuleContinueResponse
                    {
                        Value = new TaskModuleTaskInfo
                        {
                            Card = CreateAdaptiveCardAttachment(),
                            Height = 200,
                            Width = 400,
                            Title = "Adaptive Card: Inputs",
                        },
                    },
                };
            }
            return null;
        }

        private static Attachment GetProfileCard(Graph.User profile)
        {
            var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 0));

            card.Body.Add(new AdaptiveTextBlock()
            {
                Text = $"Hello, {profile.DisplayName}",
                Size = AdaptiveTextSize.ExtraLarge
            });

            card.Body.Add(new AdaptiveImage()
            {
                Url = new Uri("http://adaptivecards.io/content/cats/1.png")
            });
            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = card,
            };
        }

        private static Attachment CreateAdaptiveCardAttachment()
        {
            // combine path for cross platform support
            string[] paths = { ".", "Resources", "adaptiveCard.json" };
            var adaptiveCardJson = File.ReadAllText(Path.Combine(paths));

            var adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCardJson),
            };
            return adaptiveCardAttachment;
        }

        // Generate a set of substrings to illustrate the idea of a set of results coming back from a query. 
        private async Task<IEnumerable<(string, string, string, string, string)>> FindPackages(string text)
        {
            var obj = JObject.Parse(await (new HttpClient()).GetStringAsync($"https://azuresearch-usnc.nuget.org/query?q=id:{text}&prerelease=true"));
            return obj["data"].Select(item => (item["id"].ToString(), item["version"].ToString(), item["description"].ToString(), item["projectUrl"]?.ToString(), item["iconUrl"]?.ToString()));
        }
    }

    public class MessagingExtensionActionWithState : MessagingExtensionAction
    {
        public string State { get; set; }
    }
}
