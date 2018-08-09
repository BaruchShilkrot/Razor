﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.Razor.Tools
{
    internal class ShutdownCommand : CommandBase
    {
        public ShutdownCommand(Application parent)
            : base(parent, "shutdown")
        {
            Pipe = Option("-p|--pipe", "name of named pipe", CommandOptionType.SingleValue);
            Wait = Option("-w|--wait", "wait for shutdown", CommandOptionType.NoValue);
        }

        public CommandOption Pipe { get; }

        public CommandOption Wait { get; }

        protected override bool ValidateArguments()
        {
            if (string.IsNullOrEmpty(Pipe.Value()))
            {
                Pipe.Values.Add(PipeName.ComputeDefault());
            }

            return true;
        }

        protected async override Task<int> ExecuteCoreAsync()
        {
            Console.WriteLine($"Request to shutdown server with pipe {Pipe.Value()} received.");
            if (!IsServerRunning())
            {
                // server isn't running right now
                Out.Write("Server is not running.");
                return 0;
            }

            try
            {
                using (var client = await Client.ConnectAsync(Pipe.Value(), timeout: TimeSpan.FromSeconds(5), cancellationToken: Cancelled))
                {
                    if (client == null)
                    {
                        throw new InvalidOperationException("Couldn't connect to the server.");
                    }

                    var request = ServerRequest.CreateShutdown();
                    await request.WriteAsync(client.Stream, Cancelled).ConfigureAwait(false);

                    var response = ((ShutdownServerResponse)await ServerResponse.ReadAsync(client.Stream, Cancelled));

                    if (Wait.HasValue())
                    {
                        try
                        {
                            var process = Process.GetProcessById(response.ServerProcessId);
                            //Console.WriteLine("Start waiting...");

                            // Sometimes in linux when the process has already exited before we get here, WaitForExit tends to wait indefinitely.
                            // Add a timeout to avoid hang.
                            if (process.WaitForExit(15000))
                            {
                                Console.WriteLine($"Process {process.Id} exited after timeout");
                            }
                            else
                            {
                                Console.WriteLine($"Process {process.Id} still running after timeout. HasExited: {process.HasExited}");
                            }
                            //Console.WriteLine("Done waiting");
                        }
                        catch (Exception ex)
                        {
                            // There is an inherent race here with the server process.  If it has already shutdown
                            // by the time we try to access it then the operation has succeeded.
                            Error.Write(ex);
                            Console.WriteLine($"Some exception occured {ex} {ex.Message}");
                        }

                        Out.Write("Server pid:{0} shut down completed.", response.ServerProcessId);

                        Console.WriteLine("Server pid:{0} shut down completed.", response.ServerProcessId);
                    }
                }
            }
            catch (Exception ex) when (IsServerRunning())
            {
                // Ignore an exception that occurred while the server was shutting down.
                Error.Write(ex);
                Console.WriteLine($"Some exception occured {ex.Message}");
            }

            return 0;
        }

        private bool IsServerRunning()
        {
            if (Mutex.TryOpenExisting(MutexName.GetServerMutexName(Pipe.Value()), out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
    }
}
