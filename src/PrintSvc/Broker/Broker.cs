using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Minio;
using PrintSvc.Settings;
using PrintSvc.Storage;
using RabbitMQ.Client;

namespace PrintSvc.Broker
{
    internal class Broker : IBroker
    {
        private readonly ILogger<Broker> _logger;
        private IConnection? _connection;
        private IChannel? _channel;

        private readonly BrokerSettings _broker;

        public Broker(IOptions<BrokerSettings> brokerOptions, ILogger<Broker> logger)
        {
            _broker = brokerOptions.Value;
            _logger = logger;
        }

        public async void CreateConnection()
        {
            var factory = new ConnectionFactory
            {
                HostName = _broker.Host,
                Port = _broker.Port,
                UserName = _broker.Username,
                Password = _broker.Password
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();
        }

    }
}
