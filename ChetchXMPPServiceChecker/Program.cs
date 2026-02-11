using Chetch.ChetchXMPP;
using Chetch.Utilities;
using Chetch.Messaging;
using Microsoft.Extensions.Hosting;
using XmppDotNet.Xmpp.Sasl;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using XmppDotNet.Xmpp.XHtmlIM;
using XmppDotNet.Xmpp.AdHocCommands;

namespace ChetchXMPPServiceChecker;

class Program
{

    public class ChetchXMPPClient
    {
        public String Target { get; set; }

        public (int Left, int Top) CommandCursorStart { get; set; }
        public (int Left, int Top) MessageCursorStart { get; set; }

        public bool Subscribed { get; set; } = false;
        public bool CommandListReady { get; set; } = false;

        public String PreviousCommand { get; set; } = String.Empty;

        public bool IsConnected => cnn.CurrentState == XmppDotNet.SessionState.Binded;

        ChetchXMPPConnection cnn;

        public Task Connect(String un, String pw)
        {
            cnn = new ChetchXMPPConnection(un, pw);
            cnn.SessionStateChanged += (sender, sessionState) => {
                Console.WriteLine("Session state changed to {0}", sessionState);
            };
            cnn.MessageReceived += (sender, eargs)=>{
                var msg = eargs.Message;
                if (msg == null) return;

                switch (msg.Type)
                {
                    case MessageType.ERROR:
                        DisplayMessage(msg);
                        break;
                    
                    case MessageType.COMMAND_RESPONSE:
                        var origCommand = msg.Get<String>(ChetchXMPPMessaging.MESSAGE_FIELD_ORIGINAL_COMMAND);
                        if(origCommand == ChetchXMPPService<BackgroundService>.COMMAND_HELP)
                        {
                            Console.Clear();
                            
                            Console.WriteLine("Actions:");
                            ConsoleHelper.LF();
                            Console.WriteLine("1: Clear and list actions and commands again");
                            Console.WriteLine("2: Repeat prevous command");
                            Console.WriteLine("3: Request Service Status");
                            Console.WriteLine("4: Subscribe");
                            Console.WriteLine("5: Exit");
                            ConsoleHelper.LF();

                            Console.WriteLine("Commands:");
                            ConsoleHelper.LF();
                            var commands = msg.Get<Dictionary<String,String>>("Help");
                            foreach(var kv in commands)
                            {
                                Console.WriteLine(" > {0}: {1}", kv.Key, kv.Value);
                            }
                            ConsoleHelper.LF();
                            Console.Write("Enter an action or command: ");

                            CommandCursorStart = Console.GetCursorPosition();
                            CommandListReady = true;

                            var mcs = Console.GetCursorPosition();
                            mcs.Top += 2;
                            mcs.Left = 0;
                            MessageCursorStart = mcs;
                        }
                        else
                        {
                            DisplayMessage(msg);
                        }
                        break;

                    case MessageType.STATUS_RESPONSE:
                        DisplayMessage(msg);
                        break;

                    case MessageType.NOTIFICATION:
                        DisplayMessage(msg);
                        break;

                    case MessageType.SUBSCRIBE_RESPONSE:
                        Subscribed = true;
                        break;

                    default:
                        //DisplayMessage(msg);
                        break;
                }
            };

            ConsoleHelper.CLR("Connecting {0} to openfire server...", un);
            ConsoleHelper.LF();
            return  Task.Run(async () =>{
                var p = Console.GetCursorPosition();
                await cnn.ConnectAsync();
                Thread.Sleep(500);
                int waitCount = 1;
                while (!cnn.Ready)
                {
                    Console.WriteLine("Waiting to connect {0}...", waitCount++);
                    Console.SetCursorPosition(p.Left, p.Top);
                    Thread.Sleep(1000);
                }
                ConsoleHelper.CLR("{0} is connected!", un);
                ConsoleHelper.LF();
            });
        }

        public void Disconnect()
        {
            cnn.DisconnectAsync();
        }

        public Task GetCommandList()
        {
            CommandListReady = false;
            
            return Task.Run(async ()=> {
                await SendCommand(ChetchXMPPService<BackgroundService>.COMMAND_HELP);

                while(!CommandListReady)
                {
                    Thread.Sleep(500);
                }
            });
        }

        public async Task RequestStatus()
        {
            var msg = ChetchXMPPMessaging.CreateStatusRequestMessage(Target);
            await cnn.SendMessageAsync(msg);
        }

        public async Task Subscribe()
        {
            var cmd = ChetchXMPPMessaging.CreateSubscribeMessage(Target);
            await cnn.SendMessageAsync(cmd);
            await Task.Run(async ()=> {
                while(!Subscribed)
                {
                    Thread.Sleep(500);
                }
            });
        }

        public async Task SendCommand(String commandAndArgs)
        {
            var cmd = ChetchXMPPMessaging.CreateCommandMessage(commandAndArgs);
            cmd.Target = Target;
            await cnn.SendMessageAsync(cmd);

            PreviousCommand = commandAndArgs;
        }

        public void DisplayMessage(Message msg)
        {
            for(int i = MessageCursorStart.Top; i < Console.BufferHeight; i++)
            {
                ConsoleHelper.CLRL(i);
            }
            Console.SetCursorPosition(MessageCursorStart.Left, MessageCursorStart.Top);
            
            Console.WriteLine("Message Received:");
            ConsoleHelper.LF();

            Console.WriteLine("Type: {0} ({1})", msg.Type, msg.SubType);
            Console.WriteLine("From: {0}", msg.Sender);
            Console.WriteLine("Body:");
            foreach(var kv in msg.Values)
            {
                displayMessageElement(kv.Key, kv.Value);
            }

            Console.SetCursorPosition(CommandCursorStart.Left, CommandCursorStart.Top);
        }

        void displayMessageElement(String key, Object value)
        {
            Console.WriteLine(" {0}: {1}", key, value);
        }
    }

    const String DOMAIN = "openfire.bb.lan";
    //const String DOMAIN = "network.bulan-baru.com";
    //const String DOMAIN = "47.129.130.200";
    //const String DOMAIN = "192.168.2.88";


    const String USERNAME = "service.checker@" + DOMAIN;
    const String PASSWORD = "8ulan8aru";
    
    readonly static String[] Targets = [
                                "bbalarms.service@o" + DOMAIN,
                                "gps.service@" + DOMAIN,
                                "arduinotest.service@" + DOMAIN
                                ];

    static async Task Main(string[] args)
    {
        Console.Clear();
        
        var client = new ChetchXMPPClient();
        bool connectSuccess = false;
        try
        {
            await client.Connect(USERNAME, PASSWORD);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            ConsoleHelper.PK("Press a key to end...");
            return;
        }
        
        Console.WriteLine("Please select a target for this session...");
        for(int i = 1; i <= Targets.Length; i++)
        {
            Console.WriteLine("{0}. {1}", i, Targets[i - 1]);
        }
        String? target = null;
        ConsoleKeyInfo cki;
        do
        {
            try
            {
                cki = Console.ReadKey(true);
                int n = System.Convert.ToInt16("" + cki.KeyChar);
                if(n >= 1 && n <= Targets.Length)
                {
                    target = Targets[n - 1];
                }
                else
                {
                    throw new Exception(String.Format("{0} is not a valid selection", n));
                }
            } catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine("Try again...");
            }
        } while(target == null);

        client.Target = target;
        Console.WriteLine("Subscribing...");
        await client.Subscribe();
        Console.WriteLine("Requesting command list...");
        await client.GetCommandList();
        
        bool exitRequested = false;
        do
        {
            var line = Console.ReadLine();
            ConsoleHelper.CLRL(client.CommandCursorStart.Top, client.CommandCursorStart.Left);
            switch(line)
            {
                case "1":
                    await client.SendCommand(ChetchXMPPService<BackgroundService>.COMMAND_HELP);
                    break;

                case "2":
                    var cmd = client.PreviousCommand;
                    if(!String.IsNullOrEmpty(cmd))
                    {
                        client.SendCommand(cmd);
                    }
                    break;

                case "3":
                    await client.RequestStatus();
                    break;

                case "4":
                    await client.Subscribe();
                    break;

                case "5":
                    exitRequested = true;
                    break;

                default:
                    await client.SendCommand(line);
                    break;
            }
        } while(!exitRequested);

        Console.Clear();
        ConsoleHelper.PK("Press a key to end...");
        client.Disconnect();
    }
}
