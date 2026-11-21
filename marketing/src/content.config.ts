import { defineCollection, z } from "astro:content";
import { glob } from "astro/loaders";

// Blog collection (Astro 5 content layer). MDX bodies live in src/content/blog.
const blog = defineCollection({
  loader: glob({ pattern: "**/*.{md,mdx}", base: "./src/content/blog" }),
  schema: ({ image }) =>
    z.object({
      title: z.string(),
      description: z.string(),
      date: z.coerce.date(),
      updated: z.coerce.date().optional(),
      tags: z.array(z.string()).default([]),
      cover: image(), // local image -> optimized by astro:assets
      coverAlt: z.string(),
      author: z.object({
        name: z.string(),
        role: z.string(),
        avatar: z.string(), // path under /public (e.g. /authors/priya-nair.jpg)
      }),
      featured: z.boolean().default(false),
      draft: z.boolean().default(false),
      readingTime: z.string().default("5 min read"),
    }),
});

// Legal pages (privacy, terms, DPA, responsible AI), rendered by pages/legal/[slug].astro.
const legal = defineCollection({
  loader: glob({ pattern: "**/*.md", base: "./src/content/legal" }),
  schema: z.object({
    title: z.string(),
    description: z.string(),
    updated: z.coerce.date(),
  }),
});

export const collections = { blog, legal };
