﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramCoinMiner.Commands.Params;
using TelegramCoinMiner.Exceptions;
using TelegramCoinMiner.Extensions;
using TeleSharp.TL;

namespace TelegramCoinMiner.Commands
{
    class LaunchClickBotCommand : IAsyncCommand
    {
        public LaunchClickBotParams Params { get; set; }

        private ClickBotSwitcher _botSwitcher;
        private TLUser _currentChannel;
        private TLMessage _adMessage;
        private int _adMessageNotFoundCount = 0;
        private DateTime _startTime;

        public LaunchClickBotCommand(LaunchClickBotParams commandParams)
        {
            Params = commandParams;
            _botSwitcher = new ClickBotSwitcher();
            _currentChannel = new TLUser();
            _startTime = DateTime.Now;
        }

        public async Task Execute()
        {
            try
            {
                if (BotInfoMatchWithChannelInfo())
                {
                    _currentChannel = await GetCurrentChannel();
                    await ExecuteSendVisitCommand();
                }

                while (!Params.TokenSource.Token.IsCancellationRequested)
                {
                    _adMessage = await GetAdMessage();
                    await ExecuteWatchAdAndWaitForEndOfAdCommand();
                    if (DateTime.Now - _startTime > TimeSpan.FromMinutes(20))
                    {
                        Console.WriteLine("Прошло 20 минут, отдых");
                        await Task.Delay(TimeSpan.FromMinutes(5));
                        _startTime = DateTime.Now;
                        await ExecuteSendVisitCommand();
                        await Task.Delay(1000);
                    }
                }
            }
            catch (AdMessageNotFoundException)
            {
                _adMessageNotFoundCount++;
                if (_adMessageNotFoundCount > 10)
                {
                    _botSwitcher.Next();
                    _adMessageNotFoundCount = 0;
                }
                await ExecuteSendVisitCommand();
                await Task.Delay(1000);
            }
            catch (BrowserTimeoutException)
            {
                await ExecuteSkipCommand();
                await Task.Delay(1000);
            }
            catch (CapchaException)
            {
                await ExecuteSkipCommand();
                await Task.Delay(1000);
            }
            catch (ClickBotNotStartedException)
            {
                await ExecuteStartCommand();
                await Task.Delay(1000);
            }
            catch(TLSharp.Core.Network.Exceptions.FloodException ex) 
            {
                Console.WriteLine(ex.Message);
                await Task.Delay(ex.TimeToWait.Add(TimeSpan.FromMinutes(3)));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.GetType());
                Console.WriteLine("Непредвиденная ошибка: " + ex.Message);
            }
        }

        private bool BotInfoMatchWithChannelInfo()
        {
            return _currentChannel.FirstName != _botSwitcher.CurrentBotInfo.Title && 
                _currentChannel.Username != _botSwitcher.CurrentBotInfo.BotName;
        }

        private async Task ExecuteSendVisitCommand()
        {
            IAsyncCommand sendVisitCommand = new SendVisitCommand(new SendVisitParams
            {
                Channel = _currentChannel,
                TelegramClient = Params.TelegramClient
            });
            await sendVisitCommand.Execute();
        }

        private async Task ExecuteStartCommand()
        {
            IAsyncCommand startCommand = new SendStartCommand(new SendStartParams
            {
                Channel = _currentChannel,
                TelegramClient = Params.TelegramClient,
            });
            await startCommand.Execute();
        }

        private async Task ExecuteSkipCommand()
        {
            IAsyncCommand skipAdCommand = new SkipAdCommand(new SkipAdParams
            {
                Channel = _currentChannel,
                TelegramClient = Params.TelegramClient,
                AdMessage = _adMessage
            });
            await skipAdCommand.Execute();
        }

        private async Task<TLUser> GetCurrentChannel()
        {
            var currentBotInfo = _botSwitcher.CurrentBotInfo;
            var foundChannels = await Params.TelegramClient.SearchUserAsync(currentBotInfo.BotName);
            var currentChannel = foundChannels.Users.OfType<TLUser>().FirstOrDefault(x => x.Username == currentBotInfo.BotName && x.FirstName == currentBotInfo.Title);
            return currentChannel;
        }

        private async Task<TLMessage> GetAdMessage()
        {
            var messages = await Params.TelegramClient.GetMessages(_currentChannel, Constants.ReadMessagesCount);

            if (messages == null)
            {
                throw new ClickBotNotStartedException();
            }

            var adMessage = messages.OfType<TLMessage>().FirstOrDefault(x => x.Message.Contains("Press the \"Visit website\" button to earn"));
            if (adMessage == null)
            {
                throw new AdMessageNotFoundException();
            }
            _adMessageNotFoundCount = 0;
            return adMessage;
        }

        private async Task ExecuteWatchAdAndWaitForEndOfAdCommand()
        {
            var commands = new List<IAsyncCommand>();
            commands.Add(new WatchAdCommand(
                        new WatchAdParams
                        {
                            Channel = _currentChannel,
                            TelegramClient = Params.TelegramClient,
                            Browser = Params.Browser,
                            AdMessage = _adMessage
                        }));
            commands.Add(new WaitForTheEndOfAdCommand(
                        new WaitForTheEndOfAdParams
                        {
                            Channel = _currentChannel,
                            TelegramClient = Params.TelegramClient
                        }));
            IAsyncCommand macroCommand = new MacroCommand(commands);
            await macroCommand.Execute();
        }
    }
}
