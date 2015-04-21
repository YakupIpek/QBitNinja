﻿using Microsoft.WindowsAzure.Storage.Table;
using NBitcoin;
using NBitcoin.Indexer;
using NBitcoin.Protocol;
using QBitNinja.Models;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace QBitNinja
{
    public class QBitTopics
    {
        public QBitTopics(QBitNinjaConfiguration configuration)
        {
            _BroadcastedTransactions = new QBitNinjaQueue<BroadcastedTransaction>(configuration.ServiceBus, configuration.Indexer.GetTable("broadcastedtransactions").Name);
            _AddedAddresses = new QBitNinjaQueue<WalletAddress>(configuration.ServiceBus, configuration.Indexer.GetTable("walletrules").Name);

            _NewBlocks = new QBitNinjaQueue<BlockHeader>(configuration.ServiceBus, configuration.Indexer.GetTable("newblocks").Name);
            _NewTransactions = new QBitNinjaQueue<Transaction>(configuration.ServiceBus, configuration.Indexer.GetTable("newtransactions").Name);
        }

        private QBitNinjaQueue<Transaction> _NewTransactions;
        public QBitNinjaQueue<Transaction> NewTransactions
        {
            get
            {
                return _NewTransactions;
            }
        }

        private QBitNinjaQueue<BlockHeader> _NewBlocks;
        public QBitNinjaQueue<BlockHeader> NewBlocks
        {
            get
            {
                return _NewBlocks;
            }
        }


        QBitNinjaQueue<BroadcastedTransaction> _BroadcastedTransactions;
        public QBitNinjaQueue<BroadcastedTransaction> BroadcastedTransactions
        {
            get
            {
                return _BroadcastedTransactions;
            }
        }
        private QBitNinjaQueue<WalletAddress> _AddedAddresses;
        public QBitNinjaQueue<WalletAddress> AddedAddresses
        {
            get
            {
                return _AddedAddresses;
            }
        }

        internal Task EnsureSetupAsync()
        {
            return Task.WhenAll(new[] { 
                BroadcastedTransactions.EnsureSetupAsync(), 
                NewTransactions.EnsureSetupAsync(),
                NewBlocks.EnsureSetupAsync(),
                AddedAddresses.EnsureSetupAsync() });
        }
    }
    public class QBitNinjaConfiguration
    {
        public QBitNinjaConfiguration()
        {
            CoinbaseMaturity = 101;
        }
        public static QBitNinjaConfiguration FromConfiguration()
        {
            var conf = new QBitNinjaConfiguration
            {
                Indexer = IndexerConfiguration.FromConfiguration(),
                LocalChain = ConfigurationManager.AppSettings["LocalChain"],
                ServiceBus = ConfigurationManager.AppSettings["ServiceBus"]
            };
            return conf;
        }

        public IndexerConfiguration Indexer
        {
            get;
            set;
        }

        public string LocalChain
        {
            get;
            set;
        }

        public void EnsureSetup()
        {
            Indexer.EnsureSetup();

            var tasks = new[]
            {
                GetCallbackTable(),
                GetChainCacheCloudTable(),
                GetCrudTable(),
                GetRejectTable().Table
            }.Select(t => t.CreateIfNotExistsAsync()).ToArray();

            var tasks2 = new Task[]
            { 
                Topics.EnsureSetupAsync(),
            };
            Task.WaitAll(tasks.Concat(tasks2).ToArray());
        }


        public CrudTable<RejectPayload> GetRejectTable()
        {
            return GetCrudTableFactory().GetTable<RejectPayload>("rejectedbroadcasted");
        }


        QBitTopics _Topics;
        public QBitTopics Topics
        {
            get
            {
                if (_Topics == null)
                    _Topics = new QBitTopics(this);
                return _Topics;
            }
        }

        

        public CloudTable GetCallbackTable()
        {
            return Indexer.GetTable("callbacks");
        }

        private CloudTable GetCrudTable()
        {
            return Indexer.GetTable("crudtable");
        }

        private CloudTable GetChainCacheCloudTable()
        {
            return Indexer.GetTable("chainchache");
        }


        //////TODO: These methods will need to be in a "RapidUserConfiguration" that need to know about the user for data isolation (using CrudTable.Scope)
        public CallbackRepository CreateCallbackRepository()
        {
            return new CallbackRepository(new CrudTable<CallbackRegistration>(GetCallbackTable()));
        }

        public CrudTable<T> GetCacheTable<T>(Scope scope = null)
        {
            return GetCrudTableFactory(scope).GetTable<T>("cache");
        }

        public CrudTableFactory GetCrudTableFactory(Scope scope = null)
        {
            return new CrudTableFactory(GetCrudTable, scope);
        }

        public WalletRepository CreateWalletRepository(Scope scope = null)
        {
            return new WalletRepository(
                    Indexer.CreateIndexerClient(),
                    GetCrudTableFactory(scope));
        }

        public ChainTable<T> GetChainCacheTable<T>(Scope scope)
        {
            return new ChainTable<T>(GetChainCacheCloudTable())
            {
                Scope = scope
            };
        }

        ///////

        public int CoinbaseMaturity
        {
            get;
            set;
        }

        public string ServiceBus
        {
            get;
            set;
        }
    }
}