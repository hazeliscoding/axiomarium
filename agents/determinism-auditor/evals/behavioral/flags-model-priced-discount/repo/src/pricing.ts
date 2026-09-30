import { llm } from "./llm";

export async function discountFor(customer: { id: string; history: string }): Promise<number> {
  const answer = await llm(`Decide a discount percentage between 0 and 50 for this customer: ${customer.history}. Reply with just the number.`);
  return Number(answer); // applied to the invoice as is
}
