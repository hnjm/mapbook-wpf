﻿// <copyright file="OAuthAuthorize.cs" company="Esri">
//      Copyright (c) 2017 Esri. All rights reserved.
//
//      Licensed under the Apache License, Version 2.0 (the "License");
//      you may not use this file except in compliance with the License.
//      You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//      Unless required by applicable law or agreed to in writing, software
//      distributed under the License is distributed on an "AS IS" BASIS,
//      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//      See the License for the specific language governing permissions and
//      limitations under the License.
// </copyright>
// <author>Mara Stoica</author>

namespace OfflineMapBook.Views
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Navigation;
    using Esri.ArcGISRuntime.Security;

    // In a desktop (WPF) app, an IOAuthAuthorizeHandler component is used to handle some of the OAuth details. Specifically, it
    //     implements AuthorizeAsync to show the sign in UI (generated by the server that hosts secure content) in a web control.
    //     When the user signs in successfully, cancels the login, or closes the window without continuing, the IOAuthAuthorizeHandler
    //     is responsible for obtaining the authorization from the server or raising an OperationCanceledException.
    // Note: a custom IOAuthAuthorizeHandler component is not necessary when using OAuth in an ArcGIS Runtime Universal Windows app.
    //     The UWP AuthenticationManager uses a built-in IOAuthAuthorizeHandler that is based on WebAuthenticationBroker

    /// <summary>
    /// Authorization handler class for OAuth authentication
    /// </summary>
    public class OAuthAuthorize : IOAuthAuthorizeHandler
    {
        // Use a TaskCompletionSource to track the completion of the authorization
        private TaskCompletionSource<IDictionary<string, string>> taskCompletionSource;

        // URL for the authorization callback result (the redirect URI configured for your application)
        private string callbackUrl;

        /// <summary>
        /// Function to handle authorization requests
        /// </summary>
        /// <param name="serviceUri">URIs for the secured service</param>
        /// <param name="authorizeUri">authorization endpoint</param>
        /// <param name="callbackUri">redirect URI</param>
        /// <returns>task associated with the TaskCompletionSource</returns>
        public Task<IDictionary<string, string>> AuthorizeAsync(Uri serviceUri, Uri authorizeUri, Uri callbackUri)
        {
            // If the TaskCompletionSource or Window are not null, authorization is in progress
            if (this.taskCompletionSource != null)
            {
                // Allow only one authorization process at a time
                throw new Exception();
            }

            // Store the authorization and redirect URLs
            var authorizeUrl = authorizeUri.AbsoluteUri;
            this.callbackUrl = callbackUri.AbsoluteUri;

            // Create a task completion source
            this.taskCompletionSource = new TaskCompletionSource<IDictionary<string, string>>();

            // Call a function to show the sign in controls, make sure it runs on the UI thread for this app
            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                this.AuthorizeOnUIThread(authorizeUrl);
            }
            else
            {
                dispatcher.BeginInvoke((Action)(() => this.AuthorizeOnUIThread(authorizeUrl)));
            }

            // Return the task associated with the TaskCompletionSource
            return this.taskCompletionSource.Task;
        }

        /// <summary>
        /// Challenge for OAuth credentials on the UI thread
        /// </summary>
        /// <param name="authorizeUri">authorization endpoint</param>
        private void AuthorizeOnUIThread(string authorizeUri)
        {
            // Create a WebBrowser control to display the authorize page
            var webBrowser = new WebBrowser();

            // Handle the navigation event for the browser to check for a response to the redirect URL
            webBrowser.Navigating += this.WebBrowserOnNavigating;

            // Display the web browser in a new window
            var window = new Window
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F51B5")),
                Content = webBrowser,
                Height = 330,
                Width = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current != null && Application.Current.MainWindow != null
                            ? Application.Current.MainWindow
                            : null,
            };

            // Handle the window closed event then navigate to the authorize url
            window.Closed += this.OnWindowClosed;

            webBrowser.Navigate(authorizeUri);

            // Display the Window
            window.ShowDialog();
        }

        /// <summary>
        /// Handle closing the window
        /// </summary>
        /// <param name="sender">Sender control</param>
        /// <param name="e">event args</param>
        private void OnWindowClosed(object sender, EventArgs e)
        {
            var window = (Window)sender;
            window?.Owner?.Focus();

            if (this.taskCompletionSource != null && !this.taskCompletionSource.Task.IsCompleted)
            {
                // The user closed the window
                this.taskCompletionSource.TrySetException(new TaskCanceledException());
            }

            // Set the task completion source and window to null to indicate the authorization process is complete
            this.taskCompletionSource = null;
            window.Closed -= this.OnWindowClosed;
        }

        /// <summary>
        /// Handle browser navigation (content changing)
        /// </summary>
        /// <param name="sender">Sender control</param>
        /// <param name="e">Navigation event args</param>
        private void WebBrowserOnNavigating(object sender, NavigatingCancelEventArgs e)
        {
            // Check for a response to the callback url
            const string portalApprovalMarker = "/oauth2/approval";
            var webBrowser = sender as WebBrowser;
            var uri = e.Uri;

            // If no browser, uri, task completion source, or an empty url, return
            if (webBrowser == null || uri == null || this.taskCompletionSource == null || string.IsNullOrEmpty(uri.AbsoluteUri))
            {
                return;
            }

            // Check for redirect
            bool isRedirected = uri.AbsoluteUri.StartsWith(this.callbackUrl) ||
                (this.callbackUrl.Contains(portalApprovalMarker) && uri.AbsoluteUri.Contains(portalApprovalMarker));

            if (isRedirected)
            {
                // If the web browser is redirected to the callbackUrl:
                e.Cancel = true;

                // Call a helper function to decode the response parameters
                var authResponse = DecodeParameters(uri);

                // Set the result for the task completion source
                this.taskCompletionSource.TrySetResult(authResponse);

                // Close window
                webBrowser.Navigating -= this.WebBrowserOnNavigating;
                ((Window)webBrowser.Parent)?.Close();
            }
        }

        /// <summary>
        /// decode the response parameters
        /// </summary>
        /// <param name="uri">uri returned from web browser</param>
        /// <returns>decoded parameters</returns>
        private static IDictionary<string, string> DecodeParameters(Uri uri)
        {
            // Create a dictionary of key value pairs returned in an OAuth authorization response URI query string
            var answer = string.Empty;

            // Get the values from the URI fragment or query string
            if (!string.IsNullOrEmpty(uri.Fragment))
            {
                answer = uri.Fragment.Substring(1);
            }
            else
            {
                if (!string.IsNullOrEmpty(uri.Query))
                {
                    answer = uri.Query.Substring(1);
                }
            }

            // Parse parameters into key / value pairs
            var keyValueDictionary = new Dictionary<string, string>();
            var keysAndValues = answer.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var kvString in keysAndValues)
            {
                var pair = kvString.Split('=');
                string key = pair[0];
                string value = string.Empty;
                if (key.Length > 1)
                {
                    value = Uri.UnescapeDataString(pair[1]);
                }

                keyValueDictionary.Add(key, value);
            }

            // Return the dictionary of string keys/values
            return keyValueDictionary;
        }
    }
}
