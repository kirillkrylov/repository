﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using ATF.Repository.Exceptions;
using ATF.Repository.Mapping;
using ATF.Repository.Providers;
using ATF.Repository.Tests.Models;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Common;
using Terrasoft.Core.Entities;
using Terrasoft.Nui.ServiceModel.DataContract;
using FilterType = Terrasoft.Nui.ServiceModel.DataContract.FilterType;

namespace ATF.Repository.Tests
{
	[TestFixture]
	public class AppDataContextTests
	{
		private IDataProvider _dataProvider;
		private IAppDataContext _appDataContext;

		[SetUp]
		public void SetUp()
		{
			_dataProvider = Substitute.For<IDataProvider>();
			_appDataContext = AppDataContextFactory.GetAppDataContext(_dataProvider);
		}

		[Test]
		public void CreateModel_ShouldCreateSimpleModel()
		{
			_dataProvider.GetDefaultValues("TypedTestModel").Returns(new DefaultValuesResponse() { Success = true, DefaultValues = new Dictionary<string, object>()});
			var model = _appDataContext.CreateModel<TypedTestModel>();
			Assert.AreNotEqual(Guid.Empty, model.Id);
		}

		public class PropertyValue
		{
			public string PropertyName { get; set; }
			private string ColumnName { get; set; }
			public object DataValue { get; set; }
			private object ExpectedValue { get; set; }

			internal PropertyValue(string propertyName, object dataValue, string columnName = "",
				object expectedValue = null)
			{
				PropertyName = propertyName;
				DataValue = dataValue;
				ColumnName = columnName;
				ExpectedValue = expectedValue;
			}

			public object GetValue()
			{
				return ExpectedValue ?? DataValue;
			}

			public string GetKey()
			{
				return string.IsNullOrEmpty(ColumnName) ? PropertyName : ColumnName;
			}
		}

		private SelectQuery GetTestSelectQuery<T>(Action<Filters> enrichFilter = null) where T: BaseModel {
			var modelType = typeof(T);
			var schemaName = ModelUtilities.GetSchemaName(modelType);
			var columns = new SelectQueryColumns() {Items = new Dictionary<string, SelectQueryColumn>()};
			ModelMapper.GetModelItems(modelType).Where(modelItem =>
					modelItem.PropertyType == ModelItemType.Column || modelItem.PropertyType == ModelItemType.Lookup)
				.ForEach(property => {
					if (!columns.Items.ContainsKey(property.EntityColumnName)) {
						columns.Items.Add(property.EntityColumnName, new SelectQueryColumn() {
							Expression = new ColumnExpression() {
								ColumnPath = property.EntityColumnName,
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn
							},
							OrderDirection = OrderDirection.None,
							OrderPosition = -1
						});
					}
				});
			var filters = new Filters() {
				FilterType = FilterType.FilterGroup,
				LogicalOperation = LogicalOperationStrict.And,
				Items = new Dictionary<string, Filter>()
			};
			enrichFilter?.Invoke(filters);

			return new SelectQuery() {
				RootSchemaName = schemaName,
				AllColumns = false,
				IsDistinct = false,
				RowCount = 100,
				Columns = columns,
				Filters = filters
			};
		}

		private Filter CreateComparisonFilter(string columnPath, FilterComparisonType comparisonType,
			DataValueType dataValueType, params object[] values) {
			var rightExpressions = values.Select(value => new BaseExpression() {
				ExpressionType = EntitySchemaQueryExpressionType.Parameter,
				Parameter = new Parameter() {
					Value = GetQueryValue(value, dataValueType),
					DataValueType = dataValueType
				}
			}).ToList();
			return new Filter() {
				FilterType = rightExpressions.Count > 1 ? FilterType.InFilter : FilterType.CompareFilter,
				ComparisonType = comparisonType,
				IsEnabled = true,
				LeftExpression = new ColumnExpression() {
					ColumnPath = columnPath,
					ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn
				},
				RightExpression = rightExpressions.Count == 1 ? rightExpressions.First() : null,
				RightExpressions = rightExpressions.Count > 1 ? rightExpressions.ToArray() : null
			};
		}

		private static object GetQueryValue(object rawValue, DataValueType dataValueType) {
			if (DataValueTypeUtilities.IsDateDataValueType(dataValueType) && rawValue != null) {
				return $"\"{((DateTime)rawValue):yyyy-MM-ddTHH:mm:ss.fff}\"";
			}

			return rawValue;
		}


		private Filter CreateFilterGroup(LogicalOperationStrict logicalOperation) {
			return new Filter() {
				FilterType = FilterType.FilterGroup,
				LogicalOperation = logicalOperation,
				Items = new Dictionary<string, Filter>()
			};
		}

		#region Simple types

		private static IEnumerable<PropertyValue> TypedPropertyCases()
		{
			yield return new PropertyValue("DecimalValue", 10.0m, "DecimalValue", 10.0m);
			yield return new PropertyValue("DecimalValue", 0.0m);
			yield return new PropertyValue("DecimalValue", 1010.101m);
			yield return new PropertyValue("DecimalValue", null, "DecimalValue", 0.0m);
			yield return new PropertyValue("StringValue", "ExpectedValue");
			yield return new PropertyValue("StringValue", "");
			yield return new PropertyValue("StringValue", null);
			yield return new PropertyValue("IntValue", 100020);
			yield return new PropertyValue("IntValue", 0);
			yield return new PropertyValue("IntValue", 10.10, "IntValue", 10);
			yield return new PropertyValue("IntValue", null, "IntValue", 0);
			yield return new PropertyValue("DateTimeValue", null, "DateTimeValue", DateTime.MinValue);
			yield return new PropertyValue("DateTimeValue", DateTime.MinValue);
			yield return new PropertyValue("DateTimeValue", new DateTime(2001, 1, 1, 10, 30, 30, 100));
			yield return new PropertyValue("GuidValueId", Guid.Empty, "GuidValue");
			yield return new PropertyValue("GuidValueId", null, "GuidValue", Guid.Empty);
			yield return new PropertyValue("GuidValueId", new Guid("56208F72-DEF0-44FE-83C3-51D701A22A9E"), "GuidValue", new Guid("56208F72-DEF0-44FE-83C3-51D701A22A9E"));
			yield return new PropertyValue("BooleanValue", true);
			yield return new PropertyValue("BooleanValue", false);
			yield return new PropertyValue("BooleanValue", 0, "BooleanValue", false);
			yield return new PropertyValue("BooleanValue", 1, "BooleanValue", true);
			yield return new PropertyValue("BooleanValue", null, "BooleanValue", false);
		}

		[Test, TestCaseSource(nameof(TypedPropertyCases))]
		public void CreateModel_ShouldCreateModelAndFillSchemaProperty(PropertyValue caseData)
		{

			var key = caseData.GetKey();
			var value = caseData.GetValue();
			_dataProvider.GetDefaultValues("TypedTestModel").Returns(new DefaultValuesResponse() {
				Success = true,
				DefaultValues = new Dictionary<string, object>() {
					{key, caseData.DataValue}
				}
			});
			var model = _appDataContext.CreateModel<TypedTestModel>();
			Assert.AreNotEqual(Guid.Empty, model.Id);
			Assert.AreEqual(value, model.GetPropertyValue(caseData.PropertyName));
		}

		#endregion

		#region Lookups

		[Test]
		public void Models_WhenHasLookupAndGuidProperty_ShouldReturnExpectedValue()
		{
			var mainRecordId = Guid.NewGuid();
			var lookupRecordId = Guid.NewGuid();
			var lookupName = "LookupName";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					mainRecordId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", mainRecordId},
						{"LookupValue", lookupRecordId}
					}
				}});
			var expectedLookupSelect = GetTestSelectQuery<LookupTestModel>();
			expectedLookupSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					lookupRecordId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedLookupSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", lookupRecordId},
						{"Name", lookupName}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.Id == mainRecordId);
			var mainRecord = queryable.ToList().First();
			Assert.AreEqual(mainRecordId, mainRecord.Id);
			Assert.AreEqual(lookupRecordId, mainRecord.LookupValueId);
			Assert.AreEqual(lookupName, mainRecord.LookupValue.Name);
		}

		[Test]
		public void Models_WhenHasOnlyLookupProperty_ShouldReturnExpectedValue()
		{
			var mainRecordId = Guid.NewGuid();
			var lookupRecordId = Guid.NewGuid();
			var lookupName = "LookupName";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					mainRecordId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", mainRecordId},
						{"AnotherLookupValue", lookupRecordId}
					}
				}});
			var expectedLookupSelect = GetTestSelectQuery<LookupTestModel>();
			expectedLookupSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					lookupRecordId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedLookupSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", lookupRecordId},
						{"Name", lookupName}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.Id == mainRecordId);
			var mainRecord = queryable.ToList().First();
			Assert.AreEqual(mainRecordId, mainRecord.Id);
			Assert.AreEqual(lookupName, mainRecord.AnotherLookupValue.Name);
		}

		#endregion

		#region Filter Sources

		[Test]
		public void Models_WhenUseVariableAsFilterParameter_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			string filterNumber = "Filter";
			string expectedNumber = "Order";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.NotEqual, DataValueType.Text,
					filterNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue != filterNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUsePureValueAsFilterParameter_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			string expectedNumber = "Order";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.Equal, DataValueType.Text,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue == "Order");
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUseItemFromListAsFilterParameter_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			string expectedNumber = "Order";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.Equal, DataValueType.Text,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber}
					}
				}});
			var list = new List<string>() {expectedNumber};
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue == list.First());
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenFilterUseLookupColumnFilter_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			Guid lookupRecordId = Guid.NewGuid();
			string filteredValue = "Order";
			string expectedValue = "Order";
			string expectedLookupValue = "Order2";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("LookupValue.Name", FilterComparisonType.NotEqual, DataValueType.Text,
				filteredValue));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{ "StringValue", expectedValue},
						{"LookupValue", lookupRecordId}
					}
				}});
			var expectedLookupSelect = GetTestSelectQuery<LookupTestModel>();
			expectedLookupSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					lookupRecordId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedLookupSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", lookupRecordId},
						{"Name", expectedLookupValue}
					}
				}});
			var list = new List<string>() {filteredValue};
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x =>
				x.LookupValue.Name != list.First());
			var orders = queryable.ToList();
			var order = orders.First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedValue, order.StringValue);
			Assert.AreEqual(expectedLookupValue, order.LookupValue.Name);
		}

		[Test]
		public void Models_WhenUseIntAsDecimalInVariableType_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("DecimalValue", FilterComparisonType.Equal, DataValueType.Float2,
					(decimal)expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"DecimalValue", (decimal)expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.DecimalValue == expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.DecimalValue);
		}

		[Test]
		public void Models_WhenUseIntAsDecimalInLine_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			decimal expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("DecimalValue", FilterComparisonType.Equal, DataValueType.Float2,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"DecimalValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.DecimalValue == (int)expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.DecimalValue);
		}

		[Test]
		public void Models_WhenUseShortBooleanAsPositiveFilter_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.IsTrue(order.BooleanValue);
		}

		#endregion

		#region Filter data value type and compatison type

		[Test]
		public void Models_WhenUseStringAndEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			string expectedNumber = "Order";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.Equal, DataValueType.Text,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue == expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUseStringAndNotEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			string filteredNumber = "Filter";
			string expectedNumber = "Order";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.NotEqual, DataValueType.Text,
					filteredNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue != filteredNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUseIntAndEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("IntValue", FilterComparisonType.Equal, DataValueType.Integer,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.IntValue == expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.IntValue);
		}

		[Test]
		public void Models_WhenUseIntAndGreater_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber + 1}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.IntValue > expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber + 1, order.IntValue);
		}

		[Test]
		public void Models_WhenUseIntAndGreaterOrEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("IntValue", FilterComparisonType.GreaterOrEqual, DataValueType.Integer,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.IntValue >= expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.IntValue);
		}

		[Test]
		public void Models_WhenUseIntAndLess_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("IntValue", FilterComparisonType.Less, DataValueType.Integer,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber - 1}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.IntValue < expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber - 1, order.IntValue);
		}

		[Test]
		public void Models_WhenUseIntAndLessOrEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 1000;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("IntValue", FilterComparisonType.LessOrEqual, DataValueType.Integer,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.IntValue <= expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.IntValue);
		}

		[Test]
		public void Models_WhenUseDecimalAndEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			decimal expectedNumber = 1000.10m;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("DecimalValue", FilterComparisonType.Equal, DataValueType.Float2,
					expectedNumber));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"DecimalValue", expectedNumber}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.DecimalValue == expectedNumber);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.DecimalValue);
		}

		[Test]
		public void Models_WhenUseBoolAndEqual_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			const bool expected = true;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					expected));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", expected}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue == expected);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expected, order.BooleanValue);
		}

		[Test]
		public void Models_WhenUseBoolAndNotEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			const bool expected = true;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.NotEqual, DataValueType.Boolean,
					expected));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", false}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue != expected);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(false, order.BooleanValue);
		}

		[Test]
		public void Models_WhenUseDateTimeAndEqual_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			var expected = DateTime.Now;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("DateTimeValue", FilterComparisonType.Equal, DataValueType.DateTime,
					expected));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"DateTimeValue", expected}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.DateTimeValue == expected);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expected, order.DateTimeValue);
		}

		#endregion

		#region Not

		[Test]
		public void Models_WhenUseNotWithShortBooleanFilter_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.NotEqual, DataValueType.Boolean,
					true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", false}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => !x.BooleanValue);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.IsFalse(order.BooleanValue);
		}

		[Test]
		public void Models_WhenUseNotWithConstant_ShouldReturnExpectedValue()
		{
			Guid expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			var value = true;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					false));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", false}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue == !value);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.IsFalse(order.BooleanValue);
		}


		#endregion

		#region Sub function filters

		[Test]
		public void Models_WhenUseStartWith_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			var part = "Order";
			var expectedNumber = $"{part}Number";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.StartWith, DataValueType.Text,
					part));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber},
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue.StartsWith(part));
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUseEndsWith_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			string part = "Order";
			string expectedNumber = $"Number{part}";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.EndWith, DataValueType.Text,
					part));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber},
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue.EndsWith(part));
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUseStringContains_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			string part = "Order";
			string expectedNumber = $"Number{part}Box";
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("StringValue", FilterComparisonType.Contain, DataValueType.Text,
					part));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"StringValue", expectedNumber},
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.StringValue.Contains(part));
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.StringValue);
		}

		[Test]
		public void Models_WhenUseStartWithOnLookupProperty_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			string part = "Order";
			string expectedNumber = $"{part}Number";
			Guid lookupRecordId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("AnotherLookupValue.Name", FilterComparisonType.StartWith, DataValueType.Text,
					part));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"AnotherLookupValue", lookupRecordId},
					}
				}});
			var expectedLookupSelect = GetTestSelectQuery<LookupTestModel>();
			expectedLookupSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					lookupRecordId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedLookupSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", lookupRecordId},
						{"Name", expectedNumber}
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.AnotherLookupValue.Name.StartsWith(part));
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.AnotherLookupValue.Name);
		}

		#endregion

		#region Groups of filters

		[Test]
		public void Models_WhenUseTwoFilters_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 10;
			decimal expectedDecimalValue = 20m;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>(filters => {
				filters.Items.Add("f1",
					CreateComparisonFilter("IntValue", FilterComparisonType.Equal, DataValueType.Integer,
						expectedNumber));
				filters.Items.Add("f2",
					CreateComparisonFilter("DecimalValue", FilterComparisonType.GreaterOrEqual, DataValueType.Float2,
						expectedDecimalValue));
			});
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber},
						{"DecimalValue", expectedDecimalValue}
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.IntValue == expectedNumber && x.DecimalValue >= expectedDecimalValue);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.IntValue);
		}

		[Test]
		public void Models_WhenUseTwoFilterGroupsFiltersAndOrAnd_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 10;
			decimal expectedDecimalValue = 20m;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>(filters => {

				var filterGroup = CreateFilterGroup(LogicalOperationStrict.Or);
				filters.Items.Add("filterGroup", filterGroup);
				var filterGroup1 = CreateFilterGroup(LogicalOperationStrict.And);
				filterGroup.Items.Add("filterGroup1", filterGroup1);
				filterGroup1.Items.Add("f1",
					CreateComparisonFilter("IntValue", FilterComparisonType.Equal, DataValueType.Integer,
						expectedNumber));
				filterGroup1.Items.Add("f2",
					CreateComparisonFilter("DecimalValue", FilterComparisonType.GreaterOrEqual, DataValueType.Float2,
						expectedDecimalValue));
				var filterGroup2 = CreateFilterGroup(LogicalOperationStrict.And);
				filterGroup.Items.Add("filterGroup2", filterGroup2);
				filterGroup2.Items.Add("f1",
					CreateComparisonFilter("IntValue", FilterComparisonType.NotEqual, DataValueType.Integer,
						expectedNumber));
				filterGroup2.Items.Add("f2",
					CreateComparisonFilter("DecimalValue", FilterComparisonType.Less, DataValueType.Float2,
						expectedDecimalValue));
			});
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber},
						{"DecimalValue", expectedDecimalValue}
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x =>
				x.IntValue == expectedNumber && x.DecimalValue >= expectedDecimalValue ||
				x.IntValue != expectedNumber && x.DecimalValue < expectedDecimalValue);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.IntValue);
		}

		[Test]
		public void Models_WhenUseTwoFilterGroupsFiltersOrAndOr_ShouldReturnExpectedValue()
		{

			Guid expectedId = Guid.NewGuid();
			int expectedNumber = 10;
			decimal expectedDecimalValue = 20m;
			var expectedSelect = GetTestSelectQuery<TypedTestModel>(filters => {

				var filterGroup1 = CreateFilterGroup(LogicalOperationStrict.Or);
				filters.Items.Add("filterGroup1", filterGroup1);
				filterGroup1.Items.Add("f1",
					CreateComparisonFilter("IntValue", FilterComparisonType.Equal, DataValueType.Integer,
						expectedNumber));
				filterGroup1.Items.Add("f2",
					CreateComparisonFilter("DecimalValue", FilterComparisonType.GreaterOrEqual, DataValueType.Float2,
						expectedDecimalValue));
				var filterGroup2 = CreateFilterGroup(LogicalOperationStrict.Or);
				filters.Items.Add("filterGroup2", filterGroup2);
				filterGroup2.Items.Add("f1",
					CreateComparisonFilter("IntValue", FilterComparisonType.NotEqual, DataValueType.Integer,
						expectedNumber));
				filterGroup2.Items.Add("f2",
					CreateComparisonFilter("DecimalValue", FilterComparisonType.Less, DataValueType.Float2,
						expectedDecimalValue));
			});
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"IntValue", expectedNumber},
						{"DecimalValue", expectedDecimalValue - 1}
					}
				}});

			var queryable = _appDataContext.Models<TypedTestModel>().Where(x =>
				(x.IntValue == expectedNumber || x.DecimalValue >= expectedDecimalValue) &&
				(x.IntValue != expectedNumber || x.DecimalValue < expectedDecimalValue));
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.AreEqual(expectedNumber, order.IntValue);
		}

		#endregion

		private int TestUsedMethod(int i) {
			return i;
		}
		[Test]
		public void Models_WhenOneOfFiltersCannotBeConverted_ShouldThrowsExpressionConvertException()
		{
			Guid expectedId = Guid.NewGuid();
			var expected = new DateTime(2012, 10, 12, 14, 30, 0);
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("DateTimeValue", FilterComparisonType.Equal, DataValueType.DateTime,
					expected));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"DateTimeValue", expected}
					},
					new Dictionary<string, object>() {
						{"Id", Guid.NewGuid()},
						{"DateTimeValue", expected.AddHours(-4)}
					}
				}});
			Assert.Throws<ExpressionConvertException>(() => {
				_appDataContext.Models<TypedTestModel>().Where(x => x.IntValue == TestUsedMethod(x.IntValue)).ToList();
			});
		}

		[Test]
		[TestCase("StringValue", DataValueType.Text)]
		[TestCase("IntValue", DataValueType.Integer)]
		[TestCase("DecimalValue", DataValueType.Float2)]
		[TestCase("DateTimeValue", DataValueType.DateTime)]
		[TestCase("GuidValueId", DataValueType.Guid, "GuidValue")]
		[TestCase("BooleanValue", DataValueType.Boolean)]
		public void Models_WhenUseDateTimeAndIsNullComparison_ShouldReturnExpectedValue(string propertyName, DataValueType dataValueType, string columnName = "") {
			columnName = string.IsNullOrEmpty(columnName) ? propertyName : columnName;
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				QueryBuilderUtilities.CreateNullFilter(columnName, dataValueType));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var parameterExpression = Expression.Parameter(typeof(TypedTestModel), "model");
			var propertyType = ModelMapper.GetProperties(typeof(TypedTestModel))
				.First(p => p.PropertyName == propertyName).DataValueType;
			var nullableType = GetNullableType(propertyType);
			Expression leftExpression = Expression.Property(parameterExpression, propertyName);
			if (propertyType != nullableType) {
				leftExpression = Expression.Convert(leftExpression, nullableType);
			}
			var rightExpression = Expression.Constant(null, nullableType);
			var comparisonExpression = Expression.Equal(leftExpression, rightExpression);
			var lambda = Expression.Lambda<Func<TypedTestModel, bool>>(comparisonExpression, parameterExpression);
			var queryable = _appDataContext.Models<TypedTestModel>().Where(lambda);
			var orders = queryable.ToList();
			Assert.AreEqual(1, orders.Count);
			var order = orders.First();
			Assert.AreEqual(expectedId, order.Id);
		}

		private Type GetNullableType(Type type) {
			type = Nullable.GetUnderlyingType(type) ?? type;
			return type.IsValueType ? typeof(Nullable<>).MakeGenericType(type) : type;
		}

		[Test]
		public void Models_WhenUseWhereThenWhere_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			expectedSelect.Filters.Items.Add("f2",
				CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
					9));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue).Where(x=>x.IntValue > 9);
			var order = queryable.ToList().First();
			Assert.AreEqual(expectedId, order.Id);
			Assert.IsTrue(order.BooleanValue);
			Assert.AreEqual(10, order.IntValue);
		}

		/*[Test]
		public void Models_WhenUseWhereThenWhere2_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<Contact>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			expectedSelect.Filters.Items.Add("f2",
				CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
					9));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
					}
				}});
			var queryable = _appDataContext.Models<Contact>().Where(x =>
				x.Phone == null && x.Account.AccountCategoryId == new Guid("67c9e487-53fe-412d-800d-ff98c26f55a0") &&
				x.Account.ExactNoOfEmployees > 20).Take(10).Skip(3).OrderBy(x=>x.Name);
			var order = queryable.ToList().First();
		}*/

		[Test]
		public void Models_WhenUseWhereThenSelect_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10}
					}
				}});
			var intValue = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue).Select(x=>x.IntValue).First();
			Assert.AreEqual(10, intValue);
		}

		[Test]
		public void Models_WhenUseFirstMethod_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10}
					}
				}});
			var model = _appDataContext.Models<TypedTestModel>().First(x => x.BooleanValue);
			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseFirstOrDefaultMethod_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10}
					}
				}});
			var model = _appDataContext.Models<TypedTestModel>().FirstOrDefault(x => x.BooleanValue);
			Assert.AreEqual(expectedId, model?.Id);
		}

		[Test]
		public void Models_WhenUseWhereThenTake_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue).Take(1);
			var models = queryable.ToList();
			var first = models.FirstOrDefault();
			Assert.AreEqual(1, models.Count);
			Assert.AreEqual(10, first?.IntValue);
		}

		[Test]
		public void Models_WhenUseWhereWithThreeExpressions_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
					true));
			expectedSelect.Filters.Items.Add("f2",
				CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
					10));
			expectedSelect.Filters.Items.Add("f3",
				CreateComparisonFilter("StringValue", FilterComparisonType.StartWith, DataValueType.Text,
					"xxx"));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10}
					}
				}});
			var queryable = _appDataContext.Models<TypedTestModel>().Where(x => x.BooleanValue && x.IntValue > 10 && x.StringValue.StartsWith("xxx")).Take(1);
			var models = queryable.ToList();
			var first = models.FirstOrDefault();
			Assert.AreEqual(1, models.Count);
			Assert.AreEqual(10, first?.IntValue);
		}


		[Test]
		public void Models_WhenUseTypicalDifficultQuery_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.RowsOffset = 1;
			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			expectedSelect.Filters.Items.Add("f2", CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
				9));
			expectedSelect.Filters.Items.Add("f3", CreateComparisonFilter("IntValue", FilterComparisonType.Less, DataValueType.Integer,
				20));
			var intValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "IntValue");
			intValueColumn.OrderPosition = 1;
			intValueColumn.OrderDirection = OrderDirection.Ascending;
			var stringValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "StringValue");
			stringValueColumn.OrderPosition = 2;
			stringValueColumn.OrderDirection = OrderDirection.Descending;

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10},
						{"StringValue", "StringValue"},
						{"DateTimeValue", new DateTime(2001, 1, 1, 12, 10, 0)}
					},
					new Dictionary<string, object>() {
						{"Id", Guid.NewGuid()},
						{"BooleanValue", true},
						{"IntValue", 11},
						{"StringValue", "StringValue"},
					}
				}});
			var m = _appDataContext.Models<TypedTestModel>().Skip(1).Take(1)
				.Where(x => x.BooleanValue && x.IntValue > 9).Where(x => x.IntValue < 20).OrderBy(x => x.IntValue)
				.ThenByDescending(x => x.StringValue);
			var mList = m.ToList();
			Assert.AreEqual(2, mList.Count);
			Assert.AreEqual(10, mList.First().IntValue);
			Assert.AreEqual(11, mList.Last().IntValue);
		}

		[Test]
		public void Models_WhenUseQueryWithSkipTakeWhereAndOrderPart_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.RowsOffset = 1;

			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			expectedSelect.Filters.Items.Add("f2", CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
				9));
			expectedSelect.Filters.Items.Add("f3", CreateComparisonFilter("IntValue", FilterComparisonType.Less, DataValueType.Integer,
				20));

			var intValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "IntValue");
			intValueColumn.OrderPosition = 1;
			intValueColumn.OrderDirection = OrderDirection.Ascending;
			var stringValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "StringValue");
			stringValueColumn.OrderPosition = 2;
			stringValueColumn.OrderDirection = OrderDirection.Descending;

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10},
						{"StringValue", "StringValue"},
						{"DateTimeValue", new DateTime(2001, 1, 1, 12, 10, 0)}
					},
					new Dictionary<string, object>() {
						{"Id", Guid.NewGuid()},
						{"BooleanValue", true},
						{"IntValue", 11},
						{"StringValue", "StringValue2"}
					}
				}});
			var m = _appDataContext.Models<TypedTestModel>().Skip(1).Take(1)
				.Where(x => x.BooleanValue && x.IntValue > 9).Where(x => x.IntValue < 20).OrderBy(x => x.IntValue)
				.ThenByDescending(x => x.StringValue).Select(x => x.DateTimeValue).Select(x=>x.Hour).Select(x=>x + 10);
			var mList = m.ToList();
			Assert.AreEqual(2, mList.Count);
			Assert.AreEqual(22, mList.First());
			Assert.AreEqual(10, mList.Last());
		}

		[Test]
		public void Models_WhenUseQueryWithSkipTakeWhereAndOrderPartAndFirstOrDefaultWithoutExpression_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.RowsOffset = 1;

			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			expectedSelect.Filters.Items.Add("f2", CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
				9));
			expectedSelect.Filters.Items.Add("f3", CreateComparisonFilter("IntValue", FilterComparisonType.Less, DataValueType.Integer,
				20));

			var intValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "IntValue");
			intValueColumn.OrderPosition = 1;
			intValueColumn.OrderDirection = OrderDirection.Ascending;
			var stringValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "StringValue");
			stringValueColumn.OrderPosition = 2;
			stringValueColumn.OrderDirection = OrderDirection.Descending;

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10},
						{"StringValue", "StringValue"},
						{"DateTimeValue", new DateTime(2001, 1, 1, 12, 10, 0)}
					}
				}});
			var m = _appDataContext.Models<TypedTestModel>().Skip(1).Take(1)
				.Where(x => x.BooleanValue && x.IntValue > 9).Where(x => x.IntValue < 20).OrderBy(x => x.IntValue)
				.ThenByDescending(x => x.StringValue).FirstOrDefault();
			Assert.IsNotNull(m);
			Assert.AreEqual(10, m.IntValue);
		}

		[Test]
		public void Models_WhenUseQueryWithSkipTakeWhereAndOrderPartAndFirstOrDefaultWithExpression_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.RowsOffset = 1;

			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			expectedSelect.Filters.Items.Add("f2", CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
				9));
			expectedSelect.Filters.Items.Add("f3", CreateComparisonFilter("IntValue", FilterComparisonType.Less, DataValueType.Integer,
				20));

			var intValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "IntValue");
			intValueColumn.OrderPosition = 1;
			intValueColumn.OrderDirection = OrderDirection.Ascending;
			var stringValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "StringValue");
			stringValueColumn.OrderPosition = 2;
			stringValueColumn.OrderDirection = OrderDirection.Descending;

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10},
						{"StringValue", "StringValue"},
						{"DateTimeValue", new DateTime(2001, 1, 1, 12, 10, 0)}
					}
				}});
			var m = _appDataContext.Models<TypedTestModel>().Skip(1).Take(1)
				.Where(x => x.BooleanValue && x.IntValue > 9).OrderBy(x => x.IntValue)
				.ThenByDescending(x => x.StringValue).FirstOrDefault(x => x.IntValue < 20);
			Assert.IsNotNull(m);
			Assert.AreEqual(10, m.IntValue);
		}

		[Test]
		public void Models_WhenDetailsLinkByPropertyName_ShouldReturnExpectedValue()
		{
			var masterId = Guid.NewGuid();
			var detailId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
				masterId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", masterId}
					}
				}});
			var detailSelect = GetTestSelectQuery<TypedTestModel>();
			detailSelect.Filters.Items.Add("f1", CreateComparisonFilter("GuidValue", FilterComparisonType.Equal, DataValueType.Guid,
				masterId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(detailSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", detailId}
					}
				}});
			var master = _appDataContext.Models<TypedTestModel>().First(x=>x.Id == masterId);
			Assert.IsNotNull(master);
			Assert.AreEqual(masterId, master.Id);
			Assert.IsNotNull(master.DetailModels);
			Assert.AreEqual(1, master.DetailModels.Count);
			Assert.AreEqual(detailId, master.DetailModels.First().Id);
		}

		[Test]
		public void Models_WhenDetailsLinkByEntityColumnName_ShouldReturnExpectedValue()
		{
			var masterId = Guid.NewGuid();
			var detailId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
				masterId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", masterId}
					}
				}});
			var detailSelect = GetTestSelectQuery<TypedTestModel>();
			detailSelect.Filters.Items.Add("f1", CreateComparisonFilter("GuidValue", FilterComparisonType.Equal, DataValueType.Guid,
				masterId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(detailSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", detailId}
					}
				}});
			var master = _appDataContext.Models<TypedTestModel>().First(x=>x.Id == masterId);
			Assert.IsNotNull(master);
			Assert.AreEqual(masterId, master.Id);
			Assert.IsNotNull(master.AnotherDetailModels);
			Assert.AreEqual(1, master.AnotherDetailModels.Count);
			Assert.AreEqual(detailId, master.AnotherDetailModels.First().Id);
		}

		[Test]
		public void Models_WhenUseMaxResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"MAXValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Max,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "IntValue"
							}
						}
					}}
				}
			};

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"MAXValue", 10}
					}
				}});
			var maxValue = _appDataContext.Models<TypedTestModel>().Max(x=>x.IntValue);
			Assert.AreEqual(10, maxValue);
		}

		[Test]
		public void Models_WhenUseMinResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"MINValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Min,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "IntValue"
							}
						}
					}}
				}
			};

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"MINValue", 10}
					}
				}});
			var maxValue = _appDataContext.Models<TypedTestModel>().Min(x=>x.IntValue);
			Assert.AreEqual(10, maxValue);
		}

		[Test]
		public void Models_WhenUseAverageResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"AVERAGEValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Avg,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "IntValue"
							}
						}
					}}
				}
			};

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"AVERAGEValue", 10}
					}
				}});
			var maxValue = _appDataContext.Models<TypedTestModel>().Average(x=>x.IntValue);
			Assert.AreEqual(10, maxValue);
		}

		[Test]
		public void Models_WhenUseSumResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"SUMValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Sum,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "IntValue"
							}
						}
					}}
				}
			};

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"SUMValue", 10}
					}
				}});
			var maxValue = _appDataContext.Models<TypedTestModel>().Sum(x=>x.IntValue);
			Assert.AreEqual(10, maxValue);
		}

		[Test]
		public void Models_WhenUseCountWithInnerFilterResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"COUNTValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Count,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "IntValue"
							}
						}
					}}
				}
			};
			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"COUNTValue", 10}
					}
				}});
			var maxValue = _appDataContext.Models<TypedTestModel>().Count(model => model.BooleanValue);
			Assert.AreEqual(10, maxValue);
		}

		[Test]
		public void Models_WhenUseCountWithoutInnerFilterResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"COUNTValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Count,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "IntValue"
							}
						}
					}}
				}
			};
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"COUNTValue", 10}
					}
				}});
			var maxValue = _appDataContext.Models<TypedTestModel>().Count();
			Assert.AreEqual(10, maxValue);
		}

		[Test]
		public void Models_WhenUseAnyWithInnerFilterResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"ANYValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Count,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "Id"
							}
						}
					}}
				}
			};
			expectedSelect.Filters.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"ANYValue", 10}
					}
				}});
			var actual = _appDataContext.Models<TypedTestModel>().Any(x=>x.BooleanValue);
			Assert.AreEqual(true, actual);
		}

		[Test]
		public void Models_WhenUseAnyWithoutInnerFilterResultFunction_ShouldReturnExpectedValue()
		{
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.Columns = new SelectQueryColumns() {
				Items = new Dictionary<string, SelectQueryColumn>() {
					{"ANYValue", new SelectQueryColumn() {
						Expression = new ColumnExpression() {
							AggregationType = AggregationType.Count,
							ExpressionType = EntitySchemaQueryExpressionType.Function,
							FunctionType = FunctionType.Aggregation,
							FunctionArgument = new ColumnExpression() {
								ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
								ColumnPath = "Id"
							}
						}
					}}
				}
			};
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"ANYValue", 10}
					}
				}});
			var actual = _appDataContext.Models<TypedTestModel>().Any();
			Assert.AreEqual(true, actual);
		}

		[Test]
		public void Models_WhenRequestSameModelTwoTimes_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					expectedId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var model = _appDataContext.Models<TypedTestModel>().First(x => x.Id == expectedId);
			Assert.AreEqual(expectedId, model.Id);
			var model2 = _appDataContext.Models<TypedTestModel>().First(x => x.Id == expectedId);
			model2.IntValue = 20;
			Assert.AreSame(model, model2);
			Assert.AreEqual(20, model.IntValue);
		}

		[Test]
		public void Models_WhenUseStringInFilter_ShouldReturnExpectedValue() {
			var list = new List<string>() {"Part1", "Part2", "Part3"};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("StringValue", FilterComparisonType.Equal, DataValueType.Text,
				list.ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => list.Contains(x.StringValue));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseStringNotInFilter_ShouldReturnExpectedValue() {
			var list = new List<string>() {"Part1", "Part2", "Part3"};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("StringValue", FilterComparisonType.NotEqual, DataValueType.Text,
				list.ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => !list.Contains(x.StringValue));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseGuidInFilter_ShouldReturnExpectedValue() {
			var list = new List<Guid>() {Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("GuidValue", FilterComparisonType.Equal, DataValueType.Guid,
				list.Select(x=>(object)x).ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => list.Contains(x.GuidValueId));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseIntInFilter_ShouldReturnExpectedValue() {
			var list = new List<int>() {1, 2, 3};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("IntValue", FilterComparisonType.Equal, DataValueType.Integer,
				list.Select(x=>(object)x).ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => list.Contains(x.IntValue));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseFloatInFilter_ShouldReturnExpectedValue() {
			var list = new List<decimal>() {1.1m, 1.2m, 1.3m};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("DecimalValue", FilterComparisonType.Equal, DataValueType.Float2,
				list.Select(x=>(object)x).ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => list.Contains(x.DecimalValue));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseBoolInFilter_ShouldReturnExpectedValue() {
			var list = new List<bool>() {true, false};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				list.Select(x=>(object)x).ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => list.Contains(x.BooleanValue));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void Models_WhenUseDateTimeInFilter_ShouldReturnExpectedValue() {
			var list = new List<DateTime>() {DateTime.Now, DateTime.Now.AddDays(-10)};
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			var filter = CreateComparisonFilter("DateTimeValue", FilterComparisonType.Equal, DataValueType.DateTime,
				list.Select(x=>(object)x).ToArray());
			filter.FilterType = FilterType.InFilter;
			expectedSelect.Filters.Items.Add("f1", filter);
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});

			var model = _appDataContext.Models<TypedTestModel>().First(x => list.Contains(x.DateTimeValue));

			Assert.AreEqual(expectedId, model.Id);
		}

		[Test]
		public void ChangeTracker_WhenNoChangedExistedModel_ShouldReturnExpectedValues()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					expectedId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var model = _appDataContext.Models<TypedTestModel>().First(x => x.Id == expectedId);
			var changeTracker = _appDataContext.ChangeTracker.GetTrackedModels<TypedTestModel>();
			Assert.IsNotNull(changeTracker);
			var changeItem = changeTracker.FirstOrDefault();
			Assert.IsNotNull(changeItem);
			Assert.AreEqual(ModelState.Unchanged, changeItem.GetStatus());
			Assert.AreSame(model, changeItem.Model);
			Assert.AreEqual(0, changeItem.GetChanges().Count);
		}

		[Test]
		public void ChangeTracker_WhenChangeExistedModel_ShouldReturnExpectedValues()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					expectedId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var model = _appDataContext.Models<TypedTestModel>().First(x => x.Id == expectedId);
			model.IntValue = 10;
			var changeTracker = _appDataContext.ChangeTracker.GetTrackedModels<TypedTestModel>();
			Assert.IsNotNull(changeTracker);
			var changeItem = changeTracker.FirstOrDefault();
			Assert.IsNotNull(changeItem);
			Assert.AreSame(model, changeItem.Model);
			Assert.AreEqual(ModelState.Changed, changeItem.GetStatus());
			Assert.AreEqual(1, changeItem.GetChanges().Count);
			Assert.AreEqual(10, changeItem.GetChanges().First(x=>x.Key == "IntValue").Value);
		}

		[Test]
		public void ChangeTracker_WhenNoChangedNewModel_ShouldReturnExpectedValues()
		{
			_dataProvider.GetDefaultValues("TypedTestModel").Returns(new DefaultValuesResponse() {Success = true, DefaultValues = new Dictionary<string, object>()});
			var model = _appDataContext.CreateModel<TypedTestModel>();
			var changeTracker = _appDataContext.ChangeTracker.GetTrackedModels<TypedTestModel>();
			Assert.IsNotNull(changeTracker);
			var changeItem = changeTracker.FirstOrDefault();
			Assert.IsNotNull(changeItem);
			Assert.AreEqual(ModelState.New, changeItem.GetStatus());
			Assert.AreSame(model, changeItem.Model);
			Assert.AreEqual(0, changeItem.GetChanges().Count);
		}

		[Test]
		public void ChangeTracker_WhenChangeNewModel_ShouldReturnExpectedValues()
		{
			_dataProvider.GetDefaultValues("TypedTestModel").Returns(new DefaultValuesResponse() {Success = true, DefaultValues = new Dictionary<string, object>()});
			var model = _appDataContext.CreateModel<TypedTestModel>();
			var changeTracker = _appDataContext.ChangeTracker.GetTrackedModels<TypedTestModel>();
			model.IntValue = 10;
			Assert.IsNotNull(changeTracker);
			var changeItem = changeTracker.FirstOrDefault();
			Assert.AreSame(model, changeItem.Model);
			Assert.AreEqual(ModelState.New, changeItem.GetStatus());
			Assert.AreEqual(1, changeItem.GetChanges().Count);
			Assert.AreEqual(10, changeItem.GetChanges().First(x=>x.Key == "IntValue").Value);
		}

		[Test]
		public void ChangeTracker_WhenDeleteNewModel_ShouldReturnExpectedValues()
		{
			_dataProvider.GetDefaultValues("TypedTestModel").Returns(new DefaultValuesResponse() {Success = true, DefaultValues = new Dictionary<string, object>()});
			var model = _appDataContext.CreateModel<TypedTestModel>();
			var changeTracker = _appDataContext.ChangeTracker.GetTrackedModels<TypedTestModel>();
			model.IntValue = 10;
			_appDataContext.DeleteModel(model);
			Assert.IsNotNull(changeTracker);
			var changeItem = changeTracker.FirstOrDefault();
			Assert.AreSame(model, changeItem.Model);
			Assert.AreEqual(ModelState.Deleted, changeItem.GetStatus());
			Assert.AreEqual(1, changeItem.GetChanges().Count);
			Assert.AreEqual(10, changeItem.GetChanges().First(x=>x.Key == "IntValue").Value);
		}

		[Test]
		public void ChangeTracker_WhenDeleteExistedModel_ShouldReturnExpectedValues()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.Filters.Items.Add("f1",
				CreateComparisonFilter("Id", FilterComparisonType.Equal, DataValueType.Guid,
					expectedId));
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var model = _appDataContext.Models<TypedTestModel>().First(x => x.Id == expectedId);
			model.IntValue = 10;
			_appDataContext.DeleteModel(model);
			var changeTracker = _appDataContext.ChangeTracker.GetTrackedModels<TypedTestModel>();
			Assert.IsNotNull(changeTracker);
			var changeItem = changeTracker.FirstOrDefault();
			Assert.IsNotNull(changeItem);
			Assert.AreSame(model, changeItem.Model);
			Assert.AreEqual(ModelState.Deleted, changeItem.GetStatus());
			Assert.AreEqual(1, changeItem.GetChanges().Count);
			Assert.AreEqual(10, changeItem.GetChanges().First(x=>x.Key == "IntValue").Value);
		}


		/*[Test]
		public void Models_WhenUseDetailAnyFilterWithoutInnerFilters_ShouldReturnExpectedValue() {
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<Account>();
			expectedSelect.Filters.Items.Add("f1", new Filter() {
				FilterType = FilterType.Exists,
				ComparisonType = FilterComparisonType.Exists,
				LeftExpression = new ColumnExpression() {
					ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
					ColumnPath = "[Contact:Account].Id"
				},
				SubFilters = new Filters() {
					RootSchemaName = "Contact",
					FilterType = FilterType.FilterGroup,
					Items = new Dictionary<string, Filter>()
				}
			});
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var models = _appDataContext.Models<Account>().Where(x=>x.Contacts.Any()).ToList();
			Assert.AreEqual(1, models.Count);
			Assert.AreEqual(expectedId, models.First().Id);
		}

		[Test]
		public void Models_WhenUseDetailCountFilterWithoutInnerFilters_ShouldReturnExpectedValue() {
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<Account>();
			expectedSelect.Filters.Items.Add("f1", new Filter() {
				FilterType = FilterType.Exists,
				ComparisonType = FilterComparisonType.Exists,
				LeftExpression = new ColumnExpression() {
					ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
					ColumnPath = "[Contact:Account].Id"
				},
				SubFilters = new Filters() {
					RootSchemaName = "Contact",
					FilterType = FilterType.FilterGroup,
					Items = new Dictionary<string, Filter>()
				}
			});
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var models = _appDataContext.Models<Account>().Where(x=>x.Contacts.Count > 10).ToList();
			Assert.AreEqual(1, models.Count);
			Assert.AreEqual(expectedId, models.First().Id);
		}

		[Test]
		public void Models_WhenUseDetailCountFilterWithInnerFilters_ShouldReturnExpectedValue() {
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<Account>();
			expectedSelect.Filters.Items.Add("f1", new Filter() {
				FilterType = FilterType.Exists,
				ComparisonType = FilterComparisonType.Exists,
				LeftExpression = new ColumnExpression() {
					ExpressionType = EntitySchemaQueryExpressionType.SchemaColumn,
					ColumnPath = "[Contact:Account].Id"
				},
				SubFilters = new Filters() {
					RootSchemaName = "Contact",
					FilterType = FilterType.FilterGroup,
					Items = new Dictionary<string, Filter>()
				}
			});
			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new ItemsResponse() { Success = true, Items = new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId}
					}
				}});
			var models = _appDataContext.Models<Account>().Where(x=>x.Contacts.Where(y=>y.Age > 10).Count() > 10).ToList();
			Assert.AreEqual(1, models.Count);
			Assert.AreEqual(expectedId, models.First().Id);
		}*/
		/*[Test]
		public void ModelsTemp_Case5_ShouldReturnExpectedValue()
		{
			var expectedId = Guid.NewGuid();
			var expectedSelect = GetTestSelectQuery<TypedTestModel>();
			expectedSelect.RowCount = 1;
			expectedSelect.RowsOffset = 1;

			var filterI1 = CreateFilterGroup(LogicalOperationStrict.And);
			filterI1.Items.Add("f1", CreateComparisonFilter("BooleanValue", FilterComparisonType.Equal, DataValueType.Boolean,
				true));
			filterI1.Items.Add("f2", CreateComparisonFilter("IntValue", FilterComparisonType.Greater, DataValueType.Integer,
				9));
			expectedSelect.Filters.Items.Add("f1", filterI1);

			var filterI2 = CreateFilterGroup(LogicalOperationStrict.And);
			filterI2.Items.Add("f2", CreateComparisonFilter("IntValue", FilterComparisonType.Less, DataValueType.Integer,
				20));
			expectedSelect.Filters.Items.Add("f2", filterI2);

			var intValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "IntValue");
			intValueColumn.OrderPosition = 1;
			intValueColumn.OrderDirection = OrderDirection.Ascending;
			var stringValueColumn = expectedSelect.Columns.Items.Select(x=>x.Value).First(x => x.Expression.ColumnPath == "StringValue");
			stringValueColumn.OrderPosition = 2;
			stringValueColumn.OrderDirection = OrderDirection.Descending;

			_dataProvider
				.GetItems(Arg.Is<SelectQuery>(x => QueryComparison.AreSelectQueryEqual(expectedSelect, x)))
				.Returns(new List<Dictionary<string, object>>() {
					new Dictionary<string, object>() {
						{"Id", expectedId},
						{"BooleanValue", true},
						{"IntValue", 10},
						{"StringValue", "StringValue"},
						{"DateTimeValue", new DateTime(2001, 1, 1, 12, 10, 0)}
					}
				});
			var m = _appDataContext.Models<TypedTestModel>()
				.Where(x => x.BooleanValue && x.IntValue > 9).GroupBy(x=> new {
					x.IntValue, x.DecimalValue
				}, x=>x, (groopedValue, models) => new {
					IntValue = groopedValue.IntValue,
					DecimalValue = groopedValue.DecimalValue,
					MaxIntValue = models.Max(s=>s.IntValue),
					MinIntValue = models.Min(s=>s.IntValue),
					AverageIntValue = models.Average(s=>s.IntValue),
				});
			Assert.IsNotNull(m);
			Assert.AreEqual(10, m.First().IntValue);
		}*/
	}
}
