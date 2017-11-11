﻿using Exceptionless.Dependency;
using Exceptionless.Serializer;
using Xunit;

namespace Exceptionless.Tests.Serializer {
    public class JsonStorageSerializerTests : StorageSerializerTestBase {
        protected override void Initialize(IDependencyResolver resolver) {
            base.Initialize(resolver);
            resolver.Register<IStorageSerializer, DefaultJsonSerializer>();
        }

        protected override IStorageSerializer GetSerializer(IDependencyResolver resolver) {
            return resolver.Resolve<IStorageSerializer>();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeEnvironmentInfo() {
            base.CanSerializeEnvironmentInfo();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeRequestInfo() {
            base.CanSerializeRequestInfo();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeTraceLogEntries() {
            base.CanSerializeTraceLogEntries();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeUserInfo() {
            base.CanSerializeUserInfo();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeUserDescription() {
            base.CanSerializeUserDescription();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeManualStackingInfo() {
            base.CanSerializeManualStackingInfo();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeSimpleError() {
            base.CanSerializeSimpleError();
        }

        [Fact(Skip = "The json serializer deserialize anonymous(object) types as dictionary.")]
        public override void CanSerializeError() {
            base.CanSerializeError();
        }
    }
}
