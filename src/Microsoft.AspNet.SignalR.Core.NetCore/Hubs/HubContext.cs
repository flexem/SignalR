// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNet.SignalR.Infrastructure;
using Microsoft.AspNet.SignalR.Messaging;

namespace Microsoft.AspNet.SignalR.Hubs
{
    public struct MessageDroppedEventArgs
    {
        public string ConnectionId;
    }

    internal class HubContext : IHubContext<object>, IHubContext
    {
        public Action<MessageDroppedEventArgs> OnMessageDropped { get; set; }

        public HubContext(IConnection connection, IHubPipelineInvoker invoker, string hubName)
        {
            Clients = new HubConnectionContextBase(connection, invoker, hubName);
            Groups = new GroupManager(connection, PrefixHelper.GetHubGroupName(hubName));
            connection.OnMessageDropped = MessageDroppedCb;
        }

        private void MessageDroppedCb(Message msg)
        {
            if (OnMessageDropped == null)
                return;

            var pos = msg.Key.LastIndexOf('.');
            string connId;
            if (pos == -1)
                connId = null;
            else
            {
                pos++;
                connId = msg.Key.Substring(pos, msg.Key.Length - pos);
            }

            OnMessageDropped(new MessageDroppedEventArgs(){ConnectionId = connId});
        }

        public IHubConnectionContext<dynamic> Clients { get; private set; }

        public IGroupManager Groups { get; private set; }
    }
}
