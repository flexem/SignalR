﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Core;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.AspNet.SignalR.Json;
using Microsoft.AspNet.SignalR.Messaging;
using Microsoft.AspNet.SignalR.Tracing;
using Microsoft.AspNet.SignalR.Transports;
using Newtonsoft.Json;
using Utf8Json.Internal;
using JsonWriter = Utf8Json.JsonWriter;

namespace Microsoft.AspNet.SignalR.Infrastructure
{
    public delegate void WriteCursorDelegate(ref Utf8Json.JsonWriter writer);

    public class Connection : IConnection, ITransportConnection, ISubscriber
    {
        private readonly IMessageBus _bus;
        private readonly JsonSerializer _serializer;
        private readonly string _baseSignal;
        private readonly string _connectionId;
        private readonly IList<string> _signals;
        private readonly DiffSet<string> _groups;

        private bool _aborted;
        private bool _initializing;
        private readonly TraceSource _traceSource;
        private readonly IAckHandler _ackHandler;
        private readonly IProtectedData _protectedData;
        private readonly Func<Message, bool> _excludeMessage;
        private readonly IMemoryPool _pool;

        public Connection(IMessageBus newMessageBus,
                          JsonSerializer jsonSerializer,
                          string baseSignal,
                          string connectionId,
                          IList<string> signals,
                          IList<string> groups,
                          ITraceManager traceManager,
                          IAckHandler ackHandler,
                          IProtectedData protectedData,
                          IMemoryPool pool)
        {
            if (traceManager == null)
            {
                throw new ArgumentNullException("traceManager");
            }

            _bus = newMessageBus;
            _serializer = jsonSerializer;
            _baseSignal = baseSignal;
            _connectionId = connectionId;
            _signals = new List<string>(signals.Concat(groups));
            _groups = new DiffSet<string>(groups);
            _traceSource = traceManager["SignalR.Connection"];
            _ackHandler = ackHandler;
            _protectedData = protectedData;
            _excludeMessage = m => ExcludeMessage(m);
            _pool = pool;
        }

        public string DefaultSignal
        {
            get
            {
                return _baseSignal;
            }
        }

        IList<string> ISubscriber.EventKeys
        {
            get
            {
                return _signals;
            }
        }

        public event Action<ISubscriber, string> EventKeyAdded;

        public event Action<ISubscriber, string> EventKeyRemoved;

        public WriteCursorDelegate WriteCursor { get; set; }

        public string Identity
        {
            get
            {
                return _connectionId;
            }
        }

        private TraceSource Trace
        {
            get
            {
                return _traceSource;
            }
        }

        public Subscription Subscription
        {
            get;
            set;
        }

        public Task Send(ConnectionMessage message)
        {
            if (!String.IsNullOrEmpty(message.Signal) &&
                message.Signals != null)
            {
                throw new InvalidOperationException(
                    String.Format(CultureInfo.CurrentCulture,
                                  Resources.Error_AmbiguousMessage,
                                  message.Signal,
                                  String.Join(", ", message.Signals)));
            }

            if (message.Signals != null)
            {
                return MultiSend(message.Signals, message.Value, message.ExcludedSignals);
            }
            else
            {
                Message busMessage = CreateMessage(message.Signal, message.Value);

                busMessage.Filter = GetFilter(message.ExcludedSignals);

                if (busMessage.WaitForAck)
                {
                    Task ackTask = _ackHandler.CreateAck(busMessage.CommandId);
                    return _bus.Publish(busMessage, OnMessageDropped).Then(task => task, ackTask);
                }

                return _bus.Publish(busMessage, OnMessageDropped);
            }
        }

        public Action<Message> OnMessageDropped { get; set; }

        private Task MultiSend(IList<string> signals, object value, IList<string> excludedSignals)
        {
            if (signals.Count == 0)
            {
                // If there's nobody to send to then do nothing
                return TaskAsyncHelper.Empty;
            }

            // Serialize once
//            ArraySegment<byte> messageBuffer = GetMessageBuffer(value);
            string filter = GetFilter(excludedSignals);

            var tasks = new Task[signals.Count];

            // Send the same data to each connection id
            for (int i = 0; i < signals.Count; i++)
            {
                var message = new Message(_connectionId, signals[i], value);

                if (!String.IsNullOrEmpty(filter))
                {
                    message.Filter = filter;
                }

                tasks[i] = _bus.Publish(message, OnMessageDropped);
            }

            // Return a task that represents all
            return Task.WhenAll(tasks);
        }

        private static string GetFilter(IList<string> excludedSignals)
        {
            if (excludedSignals != null)
            {
                return String.Join("|", excludedSignals);
            }

            return null;
        }

        private Message CreateMessage(string key, object value)
        {
//            ArraySegment<byte> messageBuffer = GetMessageBuffer(value);

            var message = new Message(_connectionId, key, value);

            var command = value as Command;
            if (command != null)
            {
                // Set the command id
                message.CommandId = command.Id;
                message.WaitForAck = command.WaitForAck;
            }

            return message;
        }

//        private ArraySegment<byte> GetMessageBuffer(object value)
//        {
//            ArraySegment<byte> messageBuffer;
//            // We can't use "as" like we do for Command since ArraySegment is a struct
//            if (value is ArraySegment<byte>)
//            {
//                // We assume that any ArraySegment<byte> is already JSON serialized
//                messageBuffer = (ArraySegment<byte>)value;
//            }
//            else
//            {
//                messageBuffer = SerializeMessageValueFast(value);
//            }
//            return messageBuffer;
//        }

//        static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

//        private ArraySegment<byte> SerializeMessageValueFast(object value)
//        {
////            if (value is ClientHubInvocation hubInvocation)
////            {
////                result = Utf8Json.JsonSerializer.SerializeUnsafe<ClientHubInvocation>(hubInvocation);
////            }
////            else
////            {
//            var buf = Pool.Rent(4000);
//            var writer = new JsonWriter(buf);
//            Utf8Json.JsonSerializer.Serialize<object>(ref writer, value);
//            //            }
//
//            //            var ret = new byte[result.Count];
//            //            Buffer.BlockCopy(result.Array, result.Offset, ret, 0, result.Count);
//            //            return new ArraySegment<byte>(ret, 0, result.Count);
//            var resultbuf = writer.GetBuffer();
//            if (!ReferenceEquals(resultbuf.Array, buf))
//                Pool.Return(buf);
//            return resultbuf;
//        }

//        private ArraySegment<byte> SerializeMessageValue(object value)
//        {
//            using (var writer = new MemoryPoolTextWriter(_pool))
//            {
//                _serializer.Serialize(value, writer);
//                writer.Flush();
//
//                var data = writer.Buffer;
//
//                var buffer = new byte[data.Count];
//
//                Buffer.BlockCopy(data.Array, data.Offset, buffer, 0, data.Count);
//
//                return new ArraySegment<byte>(buffer);
//            }
//        }

        public IDisposable Receive(string messageId, Func<PersistentResponse, object, Task<bool>> callback, int maxMessages, object state)
        {
            var receiveContext = new ReceiveContext(this, callback, state);

            return _bus.Subscribe(this,
                                  messageId,
                                  (result, s) => MessageBusCallback(result, s),
                                  maxMessages,
                                  receiveContext);
        }

        private static Task<bool> MessageBusCallback(MessageResult result, object state)
        {
            var context = (ReceiveContext)state;

            return context.InvokeCallback(result);
        }

        private PersistentResponse GetResponse(MessageResult result)
        {
            // Do a single sweep through the results to process commands and extract values
            ProcessResults(result);

            Debug.Assert(WriteCursor != null, "Unable to resolve the cursor since the method is null");

            var response = new PersistentResponse(_excludeMessage, WriteCursor);
            response.Terminal = result.Terminal;

            if (!result.Terminal)
            {
                // Only set these properties if the message isn't terminal
                response.Messages = result.Messages;
                response.Aborted = _aborted;
                response.TotalCount = result.TotalCount;
                response.Initializing = _initializing;
                _initializing = false;
            }

            PopulateResponseState(response);

//            _counters.ConnectionMessagesReceivedTotal.IncrementBy(result.TotalCount);
//            _counters.ConnectionMessagesReceivedPerSec.IncrementBy(result.TotalCount);

            return response;
        }

        private bool ExcludeMessage(Message message)
        {
            if (String.IsNullOrEmpty(message.Filter))
            {
                return false;
            }

            string[] exclude = message.Filter.Split('|');

            return exclude.Any(signal => Identity.Equals(signal, StringComparison.OrdinalIgnoreCase) ||
                                                    _signals.Contains(signal) ||
                                                    _groups.Contains(signal));
        }

        private void ProcessResults(MessageResult result)
        {
            result.Messages.Enumerate<Connection>(message => message.IsCommand,
                (connection, message) => ProcessResultsCore(connection, message), this);
        }

        private static void ProcessResultsCore(Connection connection, Message message)
        {
            if (message.IsAck)
            {
                connection.Trace.TraceError("Connection {0} received an unexpected ACK message.", connection.Identity);
                return;
            }

            //var command = connection._serializer.Parse<Command>(message.Value, message.Encoding);
            var command = (Command) message.Value;
            connection.ProcessCommand(command);

            // Only send the ack if this command is waiting for it
            if (message.WaitForAck)
            {
                connection._bus.Ack(
                    acker: connection._connectionId,
                    commandId: message.CommandId).Catch(connection._traceSource);
            }
        }

        private void ProcessCommand(Command command)
        {
            switch (command.CommandType)
            {
                case CommandType.AddToGroup:
                    {
                        var name = command.Value;

                        if (EventKeyAdded != null)
                        {
                            _groups.Add(name);
                            EventKeyAdded(this, name);
                        }
                    }
                    break;
                case CommandType.RemoveFromGroup:
                    {
                        var name = command.Value;

                        if (EventKeyRemoved != null)
                        {
                            _groups.Remove(name);
                            EventKeyRemoved(this, name);
                        }
                    }
                    break;
                case CommandType.Initializing:
                    _initializing = true;
                    break;
                case CommandType.Abort:
                    _aborted = true;
                    break;
            }
        }

        private void PopulateResponseState(PersistentResponse response)
        {
            PopulateResponseState(response, _groups, _serializer, _protectedData, _connectionId);
        }

        internal static void PopulateResponseState(PersistentResponse response,
                                                   DiffSet<string> groupSet,
                                                   JsonSerializer serializer,
                                                   IProtectedData protectedData,
                                                   string connectionId)
        {
            bool anyChanges = groupSet.DetectChanges();

            if (anyChanges)
            {
                // Create a protected payload of the sorted list
                IEnumerable<string> groups = groupSet.GetSnapshot();

                // Remove group prefixes before any thing goes over the wire
                string groupsString = connectionId + ':' + serializer.Stringify(PrefixHelper.RemoveGroupPrefixes(groups)); ;

                // The groups token
                response.GroupsToken = protectedData.Protect(groupsString, Purposes.Groups);
            }
        }

        private class ReceiveContext
        {
            private readonly Connection _connection;
            private readonly Func<PersistentResponse, object, Task<bool>> _callback;
            private readonly object _callbackState;

            public ReceiveContext(Connection connection, Func<PersistentResponse, object, Task<bool>> callback, object callbackState)
            {
                _connection = connection;
                _callback = callback;
                _callbackState = callbackState;
            }

            public Task<bool> InvokeCallback(MessageResult result)
            {
                var response = _connection.GetResponse(result);

                return _callback(response, _callbackState);
            }
        }
    }
}
