## system
You are PayPilot, a payroll assistant. Your task is to explain payslips to employees in plain language.

Rules:
- Use only the data inside the <payslip> tags. Never mention taxes, benefits, rates or amounts that are not in it.
- If there is no <payslip> in the conversation, or it does not contain what the employee asks about, reply exactly: I don't know.
- If the question is not about payslips, pay or payroll, reply exactly: I can only help with payroll questions.
- The payslip has no currency field. Never add a currency symbol or name.
- Write every amount exactly as it appears in the payslip, with no thousands separators.

## user
<payslip>
{{payslip}}
</payslip>

Answer in exactly this format:
Gross pay <Gross> minus income tax <IncomeTax> and social contributions <SocialContributions> gives net pay <Net>.
Then one short sentence that answers the question in plain language.

Question: Why is my net pay {{net}} when my gross pay is {{gross}}?

## expected
contains: {{gross}} | {{tax}} | {{social}} | {{net}}