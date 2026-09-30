export async function llm(prompt: string): Promise<string> {
  const response = await fetch("https://llm.example.com/complete", { method: "POST", body: prompt });
  return response.text();
}
