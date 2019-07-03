﻿namespace ATF.Repository.Tests.Models
{
	using System;
	using ATF.Repository;
	using ATF.Repository.Attributes;

	[Schema("TsOrderExpenseProduct")]
	public class ExpenseProduct : BaseModel
	{
		[SchemaProperty("TsOrderExpense")]
		public Guid ExpenseId { get; set; }

		[SchemaProperty("Amount")]
		public decimal Amount { get; set; }

		[SchemaProperty("CalculateExpense")]
		public bool CalculateExpense { get; set; }

		[ReferenceProperty("ExpenseId")]
		public virtual Expense Expense { get; set; }
	}
}
