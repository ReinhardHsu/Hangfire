﻿using System;
using System.Collections.Generic;
using System.Linq;
using HangFire.Common;
using HangFire.Common.Filters;
using HangFire.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HangFire.Tests.Client
{
    [TestClass]
    public class JobMethodTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_ThrowsAnException_WhenTheTypeIsNull()
        {
// ReSharper disable ObjectCreationAsStatement
            new JobMethod(null, typeof (TestJob).GetMethod("Perform"));
// ReSharper restore ObjectCreationAsStatement
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_ThrowsAnException_WhenTheMethodIsNull()
        {
// ReSharper disable ObjectCreationAsStatement
            new JobMethod(typeof (TestJob), null);
// ReSharper restore ObjectCreationAsStatement
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Ctor_ThrowsAnException_WhenTheTypeDoesNotContainTheGivenMethod()
        {
// ReSharper disable ObjectCreationAsStatement
            new JobMethod(typeof (JobMethod), typeof (TestJob).GetMethod("Perform"));
// ReSharper restore ObjectCreationAsStatement
        }

        [TestMethod]
        public void Ctor_CorrectlySets_PropertyValues()
        {
            var type = typeof (TestJob);
            var methodInfo = type.GetMethod("Perform");
            var method = new JobMethod(type, methodInfo);

            Assert.AreEqual(type, method.Type);
            Assert.AreEqual(methodInfo, method.Method);
            Assert.IsFalse(method.OldFormat);
        }

        [TestMethod]
        public void Deserialize_CorrectlyDeserializes_AllTheData()
        {
            var type = typeof(TestJob);
            var methodInfo = type.GetMethod("Perform");
            var serializedData = new InvocationData
            {
                Type = type.AssemblyQualifiedName,
                Method = methodInfo.Name,
                ParameterTypes = JobHelper.ToJson(new Type[0])
            };

            var method = JobMethod.Deserialize(serializedData);

            Assert.AreEqual(type, method.Type);
            Assert.AreEqual(methodInfo, method.Method);
            Assert.IsFalse(method.OldFormat);
        }

        [TestMethod]
        [ExpectedException(typeof (ArgumentNullException))]
        public void Deserialize_ThrowsAnException_WhenSerializedDataIsNull()
        {
            JobMethod.Deserialize(null);
        }

        [TestMethod]
        [ExpectedException(typeof(JobLoadException))]
        public void Deserialize_WrapsAnException_WithTheJobLoadException()
        {
            var serializedData = new InvocationData();
            JobMethod.Deserialize(serializedData);
        }

        [TestMethod]
        [ExpectedException(typeof (JobLoadException))]
        public void Deserialize_ThrowsAnException_WhenTypeCanNotBeFound()
        {
            var serializedData = new InvocationData
            {
                Type = "NonExistingType",
                Method = "Perform",
                ParameterTypes = "",
            };

            JobMethod.Deserialize(serializedData);
        }

        [TestMethod]
        [ExpectedException(typeof(JobLoadException))]
        public void Deserialize_ThrowsAnException_WhenMethodCanNotBeFound()
        {
            var serializedData = new InvocationData
            {
                Type = typeof (TestJob).AssemblyQualifiedName,
                Method = "NonExistingMethod",
                ParameterTypes = JobHelper.ToJson(new Type[0])
            };

            JobMethod.Deserialize(serializedData);
        }

        [TestMethod]
        public void GetTypeFilterAttributes_ReturnsCorrectAttributes()
        {
            var method = GetCorrectMethod();
            var nonCachedAttributes = method.GetTypeFilterAttributes(false).ToArray();
            var cachedAttributes = method.GetTypeFilterAttributes(true).ToArray();

            Assert.AreEqual(1, nonCachedAttributes.Length);
            Assert.AreEqual(1, cachedAttributes.Length);

            Assert.IsInstanceOfType(nonCachedAttributes[0], typeof(TestTypeAttribute));
            Assert.IsInstanceOfType(cachedAttributes[1], typeof(TestTypeAttribute));
        }

        [TestMethod]
        public void GetMethodFilterAttributes_ReturnsCorrectAttributes()
        {
            var method = GetCorrectMethod();
            var nonCachedAttributes = method.GetMethodFilterAttributes(false).ToArray();
            var cachedAttributes = method.GetMethodFilterAttributes(true).ToArray();
            
            Assert.AreEqual(1, nonCachedAttributes.Length);
            Assert.AreEqual(1, cachedAttributes.Length);

            Assert.IsInstanceOfType(nonCachedAttributes[0], typeof(TestMethodAttribute));
            Assert.IsInstanceOfType(cachedAttributes[0], typeof(TestMethodAttribute));
        }

        private static JobMethod GetCorrectMethod()
        {
            var type = typeof(TestJob);
            var methodInfo = type.GetMethod("Perform");
            return new JobMethod(type, methodInfo);
        }

        #region Old Client API tests

        [TestMethod]
        public void Deserialization_FromTheOldFormat_CorrectlySerializesBothTypeAndMethod()
        {
            var serializedData = new InvocationData
            {
                Type = typeof (TestJob).AssemblyQualifiedName
            };

            var method = JobMethod.Deserialize(serializedData);
            Assert.AreEqual(typeof(TestJob), method.Type);
            Assert.AreEqual(typeof(TestJob).GetMethod("Perform"), method.Method);
            Assert.IsTrue(method.OldFormat);
        }

        [TestMethod]
        public void SerializedData_IsNotBeingChanged_DuringTheDeserialization()
        {
            var serializedData = new InvocationData
            {
                Type = typeof (TestJob).AssemblyQualifiedName
            };

            JobMethod.Deserialize(serializedData);
            Assert.IsNull(serializedData.Method);
        }

        public class TestTypeAttribute : JobFilterAttribute
        {
        }

        public class TestMethodAttribute : JobFilterAttribute
        {
        }

        [TestTypeAttribute]
        public class TestJob : BackgroundJob
        {
            [TestMethodAttribute]
            public override void Perform()
            {
            }
        }

        #endregion
    }
}