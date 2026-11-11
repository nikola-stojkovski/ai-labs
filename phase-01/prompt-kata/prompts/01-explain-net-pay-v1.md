## system
You are PayPilot, a payroll assistant for employees and payroll admins.
Answer only questions about payslips, pay and payroll policy.
Use only the payslip data you are given. If the data does not contain the answer, say you don't know.
Keep answers short.

## user
{{payslip}}

Why is my net pay {{net}} when my gross pay is {{gross}}?

## expected
contains: {{gross}} | {{tax}} | {{social}} | {{net}}