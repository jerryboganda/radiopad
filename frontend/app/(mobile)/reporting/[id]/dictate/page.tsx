import React from 'react';
import DictateClient from './DictateClient';

export function generateStaticParams() {
  return [{ id: 'new' }];
}

export default function DictatePage() {
  return <DictateClient />;
}
