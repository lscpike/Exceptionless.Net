﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exceptionless.Dependency;
using Exceptionless.Plugins;
using Exceptionless.Plugins.Default;
using Exceptionless.Models;
using Exceptionless.Models.Data;
using Exceptionless.Submission;
using Exceptionless.Tests.Utility;
using Xunit;
using Xunit.Abstractions;

namespace Exceptionless.Tests.Plugins {
    public class PluginTests {
        private readonly TestOutputWriter _writer;
        public PluginTests(ITestOutputHelper output) {
            _writer = new TestOutputWriter(output);
        }

        [Fact]
        public void ConfigurationDefaults_EnsureNoDuplicateTagsOrData() {
            var client = new ExceptionlessClient();
            var context = new EventPluginContext(client, new Event());

            var plugin = new ConfigurationDefaultsPlugin();
            plugin.Run(context);
            Assert.Equal(0, context.Event.Tags.Count);

            client.Configuration.DefaultTags.Add(Event.KnownTags.Critical);
            plugin.Run(context);
            Assert.Equal(1, context.Event.Tags.Count);
            Assert.Equal(0, context.Event.Data.Count);

            client.Configuration.DefaultData.Add("Message", new { Exceptionless = "Is Awesome!" });
            for (int index = 0; index < 2; index++) {
                plugin.Run(context);
                Assert.Equal(1, context.Event.Tags.Count);
                Assert.Equal(1, context.Event.Data.Count);
            }
        }

        [Fact]
        public void ConfigurationDefaults_IgnoredProperties() {
            var client = new ExceptionlessClient();
            client.Configuration.DefaultData.Add("Message", "Test");

            var context = new EventPluginContext(client, new Event());
            var plugin = new ConfigurationDefaultsPlugin();
            plugin.Run(context);
            Assert.Equal(1, context.Event.Data.Count);
            Assert.Equal("Test", context.Event.Data["Message"]);
            
            client.Configuration.AddDataExclusions("Ignore*");
            client.Configuration.DefaultData.Add("Ignored", "Test");
            plugin.Run(context);
            Assert.Equal(1, context.Event.Data.Count);
            Assert.Equal("Test", context.Event.Data["Message"]);
        }
        
        [Fact]
        public void IgnoreUserAgentPlugin_DiscardBot() {
            var client = new ExceptionlessClient();
            client.Configuration.AddUserAgentBotPatterns("*Bot*");
            var plugin = new IgnoreUserAgentPlugin();

            var ev = new Event();
            var context = new EventPluginContext(client, ev);
            plugin.Run(context);
            Assert.False(context.Cancel);

            ev.AddRequestInfo(new RequestInfo { UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_11_3) AppleWebKit/601.4.4 (KHTML, like Gecko) Version/9.0.3 Safari/601.4.4" });
            context = new EventPluginContext(client, ev);
            plugin.Run(context);
            Assert.False(context.Cancel);

            ev.AddRequestInfo(new RequestInfo { UserAgent = "Mozilla/5.0 (compatible; bingbot/2.0 +http://www.bing.com/bingbot.htm)" });
            context = new EventPluginContext(client, ev);
            plugin.Run(context);
            Assert.True(context.Cancel);
        }

        [Fact]
        public void HandleAggregateExceptionsPlugin_SingleInnerException() {
            var client = new ExceptionlessClient();
            var plugin = new HandleAggregateExceptionsPlugin();
            
            var exceptionOne = new Exception("one");
            var exceptionTwo = new Exception("two");

            var context = new EventPluginContext(client, new Event());
            context.ContextData.SetException(exceptionOne);
            plugin.Run(context);
            Assert.False(context.Cancel);
            
            context = new EventPluginContext(client, new Event());
            context.ContextData.SetException(new AggregateException(exceptionOne));
            plugin.Run(context);
            Assert.False(context.Cancel);
            Assert.Equal(exceptionOne, context.ContextData.GetException());

            context = new EventPluginContext(client, new Event());
            context.ContextData.SetException(new AggregateException(exceptionOne, exceptionTwo));
            plugin.Run(context);
            Assert.False(context.Cancel);
            Assert.Equal(exceptionOne, context.ContextData.GetException());
        }

        [Fact]
        public void HandleAggregateExceptionsPlugin_MultipleInnerException() {
            var submissionClient = new InMemorySubmissionClient();
            var client = new ExceptionlessClient("LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw");
            client.Configuration.Resolver.Register<ISubmissionClient>(submissionClient);
            
            var plugin = new HandleAggregateExceptionsPlugin();
            var exceptionOne = new Exception("one");
            var exceptionTwo = new Exception("two");
            
            var context = new EventPluginContext(client, new Event());
            context.ContextData.SetException(new AggregateException(exceptionOne, exceptionTwo));
            plugin.Run(context);
            Assert.True(context.Cancel);

            client.ProcessQueue();
            Assert.Equal(2, submissionClient.Events.Count);
        }

        [Fact]
        public void ErrorPlugin_DiscardDuplicates() {
            var errorPlugins = new List<IEventPlugin> {
                new ErrorPlugin(),
                new SimpleErrorPlugin()
            };

            foreach (var plugin in errorPlugins) {
                var exception = new Exception("Nested", new MyApplicationException("Test") {
                    IgnoredProperty = "Test",
                    RandomValue = "Test"
                });

                var client = new ExceptionlessClient();
                var context = new EventPluginContext(client, new Event());
                context.ContextData.SetException(exception);
                plugin.Run(context);
                Assert.False(context.Cancel);

                IData error = context.Event.GetError() as IData ?? context.Event.GetSimpleError();
                Assert.NotNull(error);

                context = new EventPluginContext(client, new Event());
                context.ContextData.SetException(exception);
                plugin.Run(context);
                Assert.True(context.Cancel);
                
                error = context.Event.GetError() as IData ?? context.Event.GetSimpleError();
                Assert.Null(error);
            }
        }

        public static IEnumerable<object[]> DifferentExceptionDataDictionaryTypes {
            get {
                return new[] {
                    new object[] { null, false, 0 },
                    new object[] { new Dictionary<object, object> { { (object)1, (object)1 } }, true, 1 },
                    new object[] { new Dictionary<PriorityAttribute, PriorityAttribute>() { { new PriorityAttribute(1), new PriorityAttribute(1) } }, false, 1 },
                    new object[] { new Dictionary<int, int> { { 1, 1 } }, false, 1 },
                    new object[] { new Dictionary<bool, bool> { { false, false } }, false, 1 },
                    new object[] { new Dictionary<Guid, Guid> { { Guid.Empty, Guid.Empty } }, false, 1 },
                    new object[] { new Dictionary<IData, IData> { { new SimpleError(), new SimpleError() } }, false, 1 },
                    new object[] { new Dictionary<TestEnum, TestEnum> { { TestEnum.None, TestEnum.None } }, false, 1 },
                    new object[] { new Dictionary<TestStruct, TestStruct> { { new TestStruct(), new TestStruct() } }, false, 1 },
                    new object[] { new Dictionary<string, string> { { "test", "string" } }, true, 1 },
                    new object[] { new Dictionary<string, object> { { "test", "object" } }, true, 1 },
                    new object[] { new Dictionary<string, PriorityAttribute> { { "test", new PriorityAttribute(1) } }, true, 1 },
                    new object[] { new Dictionary<string, Guid> { { "test", Guid.Empty } }, true, 1 },
                    new object[] { new Dictionary<string, IData> { { "test", new SimpleError() } }, true, 1 },
                    new object[] { new Dictionary<string, TestEnum> { { "test", TestEnum.None } }, true, 1 },
                    new object[] { new Dictionary<string, TestStruct> { { "test", new TestStruct() } }, true, 1 },
                    new object[] { new Dictionary<string, int> { { "test", 1 } }, true, 1 },
                    new object[] { new Dictionary<string, bool> { { "test", false } }, true, 1 }
                };
            }
        }

        [Theory]
        [MemberData("DifferentExceptionDataDictionaryTypes")]
        public void ErrorPlugin_CanProcessDifferentExceptionDataDictionaryTypes(IDictionary data, bool canMarkAsProcessed, int processedDataItemCount) {
            var errorPlugins = new List<IEventPlugin> {
                new ErrorPlugin(),
                new SimpleErrorPlugin()
            };

            foreach (var plugin in errorPlugins) {
                if (data != null && data.Contains("@exceptionless"))
                    data.Remove("@exceptionless");

                var exception = new MyApplicationException("Test") { SetsDataProperty = data };
                var client = new ExceptionlessClient();
                client.Configuration.AddDataExclusions("SetsDataProperty");
                var context = new EventPluginContext(client, new Event());
                context.ContextData.SetException(exception);
                plugin.Run(context);
                Assert.False(context.Cancel);

                Assert.Equal(canMarkAsProcessed, exception.Data != null && exception.Data.Contains("@exceptionless"));

                IData error = context.Event.GetError() as IData ?? context.Event.GetSimpleError();
                Assert.NotNull(error);
                Assert.Equal(processedDataItemCount, error.Data.Count);
            }
        }
        
        [Fact]
        public void ErrorPlugin_CopyExceptionDataToRootErrorData() {
            var errorPlugins = new List<IEventPlugin> {
                new ErrorPlugin(),
                new SimpleErrorPlugin()
            };

            foreach (var plugin in errorPlugins) {
                var exception = new MyApplicationException("Test") {
                    RandomValue = "Test",
                    SetsDataProperty = new Dictionary<object, string> {
                        { 1, 1.GetType().Name },
                        { "test", "test".GetType().Name },
                        { Guid.NewGuid(), typeof(Guid).Name },
                        { false, typeof(bool).Name }
                    } 
                };

                var client = new ExceptionlessClient();
                var context = new EventPluginContext(client, new Event());
                context.ContextData.SetException(exception);
                plugin.Run(context);
                Assert.False(context.Cancel);

                IData error = context.Event.GetError() as IData ?? context.Event.GetSimpleError();
                Assert.NotNull(error);
                Assert.Equal(5, error.Data.Count);
            }
        }

        [Fact]
        public void ErrorPlugin_IgnoredProperties() {
            var exception = new MyApplicationException("Test") {
                IgnoredProperty = "Test",
                RandomValue = "Test"
            };
            
            var errorPlugins = new List<IEventPlugin> {
                new ErrorPlugin(),
                new SimpleErrorPlugin()
            };

            foreach (var plugin in errorPlugins) {
                var client = new ExceptionlessClient();
                var context = new EventPluginContext(client, new Event());
                context.ContextData.SetException(exception);

                plugin.Run(context);
                IData error = context.Event.GetError() as IData ?? context.Event.GetSimpleError();
                Assert.NotNull(error);
                Assert.True(error.Data.ContainsKey(Error.KnownDataKeys.ExtraProperties));
                var json = error.Data[Error.KnownDataKeys.ExtraProperties] as string;
                Assert.Equal("{\"ignored_property\":\"Test\",\"random_value\":\"Test\"}", json);

                client.Configuration.AddDataExclusions("Ignore*");
                context = new EventPluginContext(client, new Event());
                context.ContextData.SetException(exception);

                plugin.Run(context);
                error = context.Event.GetError() as IData ?? context.Event.GetSimpleError();
                Assert.NotNull(error);
                Assert.True(error.Data.ContainsKey(Error.KnownDataKeys.ExtraProperties));
                json = error.Data[Error.KnownDataKeys.ExtraProperties] as string;
                Assert.Equal("{\"random_value\":\"Test\"}", json);
            }
        }

        [Fact]
        public void EnvironmentInfo_CanRunInParallel() {
            var client = new ExceptionlessClient();
            var ev = new Event { Type = Event.KnownTypes.Session };
            var plugin = new EnvironmentInfoPlugin();

            Parallel.For(0, 10000, i => {
                var context = new EventPluginContext(client, ev);
                plugin.Run(context);
                Assert.Equal(1, context.Event.Data.Count);
                Assert.NotNull(context.Event.Data[Event.KnownDataKeys.EnvironmentInfo]);
            });
        }

        [Fact]
        public void EnvironmentInfo_ShouldAddSessionStart() {
            var client = new ExceptionlessClient();
            var context = new EventPluginContext(client, new Event { Type = Event.KnownTypes.Session });
         
            var plugin = new EnvironmentInfoPlugin();
            plugin.Run(context);
            Assert.Equal(1, context.Event.Data.Count);
            Assert.NotNull(context.Event.Data[Event.KnownDataKeys.EnvironmentInfo]);
        }

        [Fact]
        public void CanCancel() {
            var client = new ExceptionlessClient();
            foreach (var plugin in client.Configuration.Plugins)
                client.Configuration.RemovePlugin(plugin.Key);

            client.Configuration.AddPlugin("cancel", 1, ctx => ctx.Cancel = true);
            client.Configuration.AddPlugin("add-tag", 2, ctx => ctx.Event.Tags.Add("Was Not Canceled"));

            var context = new EventPluginContext(client, new Event());
            EventPluginManager.Run(context);
            Assert.True(context.Cancel);
            Assert.Equal(0, context.Event.Tags.Count);
        }

        [Fact]
        public void ShouldUseReferenceIds() {
            var client = new ExceptionlessClient();
            foreach (var plugin in client.Configuration.Plugins)
                client.Configuration.RemovePlugin(plugin.Key);

            var context = new EventPluginContext(client, new Event { Type = Event.KnownTypes.Error });
            EventPluginManager.Run(context);
            Assert.Null(context.Event.ReferenceId);

            client.Configuration.UseReferenceIds();
            context = new EventPluginContext(client, new Event { Type = Event.KnownTypes.Error });
            EventPluginManager.Run(context);
            Assert.NotNull(context.Event.ReferenceId);
        }

        [Fact]
        public void PrivateInformation_WillSetIdentity() {
            var client = new ExceptionlessClient();
            var plugin = new SetEnvironmentUserPlugin();

            var context = new EventPluginContext(client, new Event { Type = Event.KnownTypes.Log, Message = "test" });
            plugin.Run(context);

            var user = context.Event.GetUserIdentity();
            Assert.Equal(Environment.UserName, user.Identity);
        }
        
        [Fact]
        public void PrivateInformation_WillNotUpdateIdentity() {
            var client = new ExceptionlessClient();
            var plugin = new SetEnvironmentUserPlugin();

            var ev = new Event { Type = Event.KnownTypes.Log, Message = "test" };
            ev.SetUserIdentity(null, "Blake");
            var context = new EventPluginContext(client, ev);
            plugin.Run(context);

            var user = context.Event.GetUserIdentity();
            Assert.Null(user.Identity);
            Assert.Equal("Blake", user.Name);
        }

        [Theory]
        [InlineData(Event.KnownTypes.Error, null, false)]
        [InlineData(Event.KnownTypes.FeatureUsage, null, false)]
        [InlineData(Event.KnownTypes.Log, null, false)]
        [InlineData(Event.KnownTypes.NotFound, null, false)]
        [InlineData(Event.KnownTypes.Session, null, true)]
        [InlineData(Event.KnownTypes.Session, "123456789", false)]
        [InlineData(Event.KnownTypes.SessionEnd, null, true)]
        [InlineData(Event.KnownTypes.SessionEnd, "123456789", false)]
        [InlineData(Event.KnownTypes.SessionHeartbeat, null, true)]
        [InlineData(Event.KnownTypes.SessionHeartbeat, "123456789", false)]
        public void CancelSessionsWithNoUserTest(string eventType, string identity, bool cancelled) {
            var ev = new Event { Type = eventType };
            ev.SetUserIdentity(identity);

            var context = new EventPluginContext(new ExceptionlessClient(), ev);
            var plugin = new CancelSessionsWithNoUserPlugin();
            plugin.Run(context);
            Assert.Equal(cancelled, context.Cancel);
        }
        
        [Fact]
        public void LazyLoadAndRemovePlugin() {
            var configuration = new ExceptionlessConfiguration(DependencyResolver.Default);
            foreach (var plugin in configuration.Plugins)
                configuration.RemovePlugin(plugin.Key);

            configuration.AddPlugin<ThrowIfInitializedTestPlugin>();
            configuration.RemovePlugin<ThrowIfInitializedTestPlugin>();
        }

        private class ThrowIfInitializedTestPlugin : IEventPlugin, IDisposable {
            public ThrowIfInitializedTestPlugin() {
                throw new ApplicationException("Plugin shouldn't be constructed");
            }

            public void Run(EventPluginContext context) {}
            
            public void Dispose() {
                throw new ApplicationException("Plugin shouldn't be created or disposed");
            }
        }

        [Fact]
        public void CanDisposePlugin() {
            var configuration = new ExceptionlessConfiguration(DependencyResolver.Default);
            foreach (var plugin in configuration.Plugins)
                configuration.RemovePlugin(plugin.Key);

            Assert.Equal(0, CounterTestPlugin.ConstructorCount);
            Assert.Equal(0, CounterTestPlugin.RunCount);
            Assert.Equal(0, CounterTestPlugin.DisposeCount);

            configuration.AddPlugin<CounterTestPlugin>();
            configuration.AddPlugin<CounterTestPlugin>();

            for (int i = 0; i < 2; i++) {
                foreach (var pluginRegistration in configuration.Plugins)
                    pluginRegistration.Plugin.Run(new EventPluginContext(new ExceptionlessClient(), new Event()));
            }

            configuration.RemovePlugin<CounterTestPlugin>();
            configuration.RemovePlugin<CounterTestPlugin>();


            Assert.Equal(1, CounterTestPlugin.ConstructorCount);
            Assert.Equal(2, CounterTestPlugin.RunCount);
            Assert.Equal(1, CounterTestPlugin.DisposeCount);
        }

        public class CounterTestPlugin : IEventPlugin, IDisposable {
            public static byte ConstructorCount = 0;
            public static byte RunCount = 0;
            public static byte DisposeCount = 0;

            public CounterTestPlugin() {
                ConstructorCount++;
            }

            public void Run(EventPluginContext context) {
                RunCount++;
            }
            
            public void Dispose() {
                DisposeCount++;
            }
        }

        [Fact]
        public void VerifyPriority() {
            var config = new ExceptionlessConfiguration(DependencyResolver.CreateDefault());
            foreach (var plugin in config.Plugins)
                config.RemovePlugin(plugin.Key);

            Assert.Equal(0, config.Plugins.Count());
            config.AddPlugin<EnvironmentInfoPlugin>();
            config.AddPlugin<PluginWithPriority11>();
            config.AddPlugin<PluginWithNoPriority>();
            config.AddPlugin("version", 1, ctx => ctx.Event.SetVersion("1.0.0.0"));
            config.AddPlugin("version2", 2, ctx => ctx.Event.SetVersion("1.0.0.0"));
            config.AddPlugin("version3", 3, ctx => ctx.Event.SetVersion("1.0.0.0"));

            var plugins = config.Plugins.ToArray();
            Assert.Equal(typeof(PluginWithNoPriority), plugins[0].Plugin.GetType());
            Assert.Equal("version", plugins[1].Key);
            Assert.Equal("version2", plugins[2].Key);
            Assert.Equal("version3", plugins[3].Key);
            Assert.Equal(typeof(PluginWithPriority11), plugins[4].Plugin.GetType());
            Assert.Equal(typeof(EnvironmentInfoPlugin), plugins[5].Plugin.GetType());
        }

        [Fact]
        public void ViewPriority() {
            var config = new ExceptionlessConfiguration(DependencyResolver.CreateDefault());
            foreach (var plugin in config.Plugins)
                _writer.WriteLine(plugin);
        }

        public class PluginWithNoPriority : IEventPlugin {
            public void Run(EventPluginContext context) {}
        }

        [Priority(11)]
        public class PluginWithPriority11 : IEventPlugin {
            public void Run(EventPluginContext context) {}
        }

        private enum TestEnum {
            None = 1
        }

        private struct TestStruct {
            public int Id { get; set; }
        }
        
        public class MyApplicationException : ApplicationException {
            public MyApplicationException(string message) : base(message) {
                SetsDataProperty = Data;
            }

            public string IgnoredProperty { get; set; }

            public string RandomValue { get; set; }
            
            public IDictionary SetsDataProperty { get; set; }

            public override IDictionary Data { get { return SetsDataProperty; }  }
        }
    }
}