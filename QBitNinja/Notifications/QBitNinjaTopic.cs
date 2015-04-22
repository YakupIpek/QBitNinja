﻿using Microsoft.Data.OData;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QBitNinja.Notifications;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace QBitNinja.Notifications
{

    public class MessageControl
    {

        internal DateTime? _Scheduled;
        public MessageControl()
        {
        }
        public void RescheduleIn(TimeSpan delta)
        {
            RescheduleFor(DateTime.UtcNow + delta);
        }

        public void RescheduleFor(DateTime date)
        {
            date = date.ToUniversalTime();
            _Scheduled = date;
        }
    }
    public class QBitNinjaTopicConsumer<T> where T : class
    {
        private QBitNinjaTopic<T> _Parent;
        private SubscriptionCreation subscriptionCreation;

        internal QBitNinjaTopicConsumer(QBitNinjaTopic<T> parent, SubscriptionCreation subscriptionCreation)
        {
            if (subscriptionCreation.TopicPath == null || subscriptionCreation.Name == null)
                throw new ArgumentException("Missing informations in subscription creation", "subscriptionCreation");
            this._Parent = parent;
            this.subscriptionCreation = subscriptionCreation;
        }

        public async Task DrainMessages()
        {
            while (await ReceiveAsync().ConfigureAwait(false) != null)
            {
            }
        }

        public async Task<T> ReceiveAsync(TimeSpan? timeout = null)
        {
            if (timeout == null)
                timeout = TimeSpan.Zero;
            var client = CreateSubscriptionClient();
            BrokeredMessage message = null;
            message = await client.ReceiveAsync(timeout.Value).ConfigureAwait(false);
            return ToObject(message);
        }

        public SubscriptionClient CreateSubscriptionClient()
        {
            var client = SubscriptionClient.CreateFromConnectionString(_Parent.ConnectionString, subscriptionCreation.TopicPath, subscriptionCreation.Name, ReceiveMode.ReceiveAndDelete);
            return client;
        }

        public async Task EnsureExistsAndDrainedAsync()
        {
            var subscription = await _Parent.GetNamespace().EnsureSubscriptionExistsAsync(subscriptionCreation).ConfigureAwait(false);
            await DrainMessages().ConfigureAwait(false);
        }

        public QBitNinjaTopicConsumer<T> EnsureExists()
        {
            try
            {

                _Parent.GetNamespace().EnsureSubscriptionExistsAsync(subscriptionCreation).Wait();
            }
            catch (AggregateException aex)
            {
                ExceptionDispatchInfo.Capture(aex.InnerException).Throw();
            }
            return this;
        }

        public IDisposable OnMessage(Action<T> evt)
        {
            return OnMessage((a, b) => evt(a));
        }
        public IDisposable OnMessage(Action<T, MessageControl> evt)
        {
            var client = CreateSubscriptionClient();
            client.OnMessage(bm =>
            {
                var control = new MessageControl();
                var obj = ToObject(bm);
                if (obj == null)
                    return;
                evt(obj, control);
                if (control._Scheduled != null)
                {
                    BrokeredMessage message = new BrokeredMessage(Serializer.ToString(obj));
                    message.MessageId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(32));
                    message.ScheduledEnqueueTimeUtc = control._Scheduled.Value;
                    _Parent.CreateTopicClient().Send(message);
                }
            }, new OnMessageOptions()
            {
                AutoComplete = true,
                MaxConcurrentCalls = 1
            });
            return new ActionDisposable(() => client.Close());
        }

        private static T ToObject(BrokeredMessage bm)
        {
            if (bm == null)
                return default(T);
            var result = bm.GetBody<string>();
            var obj = Serializer.ToObject<T>(result);
            return obj;
        }

    }

    public class QBitNinjaTopic<T> where T : class
    {
        public QBitNinjaTopic(string connectionString, string topic)
            : this(connectionString, new TopicCreation()
            {
                Path = topic
            })
        {

        }
        public QBitNinjaTopic(string connectionString, TopicCreation topic, SubscriptionCreation defaultSubscription = null)
        {
            _Topic = topic;
            _DefaultSubscription = defaultSubscription;
            _ConnectionString = connectionString;
        }

        SubscriptionCreation _DefaultSubscription;

        public async Task<bool> AddAsync(T entity)
        {
            var client = CreateTopicClient();
            var str = Serializer.ToString<T>(entity);
            BrokeredMessage brokered = new BrokeredMessage(str);
            if (_Topic.RequiresDuplicateDetection.HasValue &&
                _Topic.RequiresDuplicateDetection.Value)
            {
                if (GetMessageId == null)
                    throw new InvalidOperationException("Requires Duplicate Detection is on, but the callback GetMessageId is not set");
                brokered.MessageId = GetMessageId(entity);
            }
            await client.SendAsync(brokered).ConfigureAwait(false);
            return true;
        }

        internal TopicClient CreateTopicClient()
        {
            var client = TopicClient.CreateFromConnectionString(ConnectionString, Topic);
            return client;
        }
        public QBitNinjaTopicConsumer<T> CreateConsumer(string subscriptionName = null)
        {
            return CreateConsumer(new SubscriptionCreation()
            {
                Name = subscriptionName,
            });
        }

        public Func<T, string> GetMessageId
        {
            get;
            set;
        }

        public QBitNinjaTopicConsumer<T> CreateConsumer(SubscriptionCreation subscriptionDescription)
        {
            if (subscriptionDescription == null)
                throw new ArgumentNullException("subscriptionDescription");
            if (subscriptionDescription.Name == null)
                subscriptionDescription.Name = GetMac();
            subscriptionDescription.TopicPath = _Topic.Path;
            subscriptionDescription.Merge(_DefaultSubscription);
            return new QBitNinjaTopicConsumer<T>(this, subscriptionDescription);
        }

        private string GetMac()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            PhysicalAddress address = nics[0].GetPhysicalAddress();
            byte[] bytes = address.GetAddressBytes();
            return Encoders.Hex.EncodeData(bytes);
        }

        private readonly TopicCreation _Topic;
        public string Topic
        {
            get
            {
                return _Topic.Path;
            }
        }
        private readonly string _ConnectionString;
        public string ConnectionString
        {
            get
            {
                return _ConnectionString;
            }
        }



        internal Task EnsureSetupAsync()
        {
            return GetNamespace().EnsureTopicExistAsync(_Topic);
        }

        public NamespaceManager GetNamespace()
        {
            return NamespaceManager.CreateFromConnectionString(ConnectionString);
        }
    }
}