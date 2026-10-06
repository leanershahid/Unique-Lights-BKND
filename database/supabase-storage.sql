-- Unique Lights: Supabase Storage setup for product images.
-- Run once in the Supabase SQL editor (Dashboard -> SQL Editor) as the project owner.
--
-- * Creates a PUBLIC bucket "product-images" (5 MB limit, jpg/png/webp only).
-- * Anyone can read objects (images are served from /storage/v1/object/public/product-images/...).
-- * Nobody can insert/update/delete through the anon or authenticated roles. The API writes with the
--   service_role key, which bypasses Row Level Security, so no write policies are needed (or wanted).
-- Idempotent: safe to run more than once.

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
    'product-images',
    'product-images',
    true,
    5242880,
    array['image/jpeg', 'image/png', 'image/webp']
)
on conflict (id) do update
set public             = excluded.public,
    file_size_limit    = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

-- Public read access to objects in this bucket.
drop policy if exists "Public read product images" on storage.objects;
create policy "Public read product images"
    on storage.objects
    for select
    to public
    using (bucket_id = 'product-images');

-- Explicitly make sure no client-side write policies exist for this bucket.
-- (RLS is enabled on storage.objects by Supabase; without an insert/update/delete policy,
--  anon/authenticated users cannot write. The service_role key used by the API bypasses RLS.)
drop policy if exists "Service role manages product images" on storage.objects;
drop policy if exists "Authenticated upload product images" on storage.objects;
drop policy if exists "Authenticated update product images" on storage.objects;
drop policy if exists "Authenticated delete product images" on storage.objects;
