using Microsoft.Extensions.AI;
using System.Globalization;

namespace Phase02.ProviderSwitch.Core;

public sealed class PayslipExplainer(IChatClient chat)
{
    // v5 from Phase 1: the system prompt holds only the rules that are true in every conversation.
    private const string SystemPrompt = """
        You are PayPilot, a payroll assistant. You answer questions about the employee's payslip.
        Rules:
        - Use only the data inside <payslip>. If the answer is not there, reply exactly: I don't know.
        - Never add a currency symbol. Never use thousands separators.
        - At most 3 sentences.
        """;

    public async Task<ExplainResult> ExplainNetPayAsync(
        PayslipAmounts payslip,
        string question,
        CancellationToken cancellationToken = default)
    {
        // The template travels with the data: code adds it only when there is a payslip.
        var userMessage = string.Create(CultureInfo.InvariantCulture, $"""
            <payslip>
            Gross: {payslip.Gross}
            IncomeTax: {payslip.IncomeTax}
            SocialContributions: {payslip.SocialContributions}
            Net: {payslip.Net}
            </payslip>

            Answer in exactly this format: "Gross pay <Gross> minus income tax <IncomeTax> and social contributions <SocialContributions> gives net pay <Net>." Then add one short sentence in plain language.

            Question: {question}
            """);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage),
        ];

        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 }, cancellationToken);

        return new ExplainResult(
            response.Text,
            response.ModelId,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);
    }
}
