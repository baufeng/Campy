// Copyright (c) 2012 DotNetAnywhere
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

#if !defined(__HEAP_H)
#define __HEAP_H

#include "MetaData.h"
#include "Types.h"

typedef struct tHeapRoots_ tHeapRoots;
typedef struct tHeapRootEntry_ tHeapRootEntry;

#ifdef DIAG_GC
extern U64 gcTotalTime;
#endif

struct tHeapRootEntry_ {
	U32 numPointers; // The number of pointers within this memory area
	void **pMem;
};

struct tHeapRoots_ {
	U32 capacity;
	U32 num;
	tHeapRootEntry *pHeapEntries;
};

function_space_specifier void Heap_Init();
function_space_specifier void Heap_SetRoots(tHeapRoots *pHeapRoots, void *pRoots, U32 sizeInBytes);
function_space_specifier void Heap_UnmarkFinalizer(HEAP_PTR heapPtr);
function_space_specifier void Heap_GarbageCollect();
function_space_specifier U32 Heap_NumCollections();
function_space_specifier U32 Heap_GetTotalMemory();

function_space_specifier HEAP_PTR Heap_Alloc(tMD_TypeDef *pTypeDef, U32 size);
function_space_specifier HEAP_PTR Heap_AllocType(tMD_TypeDef *pTypeDef);
function_space_specifier void Heap_MakeUndeletable(HEAP_PTR heapEntry);
function_space_specifier void Heap_MakeDeletable(HEAP_PTR heapEntry);

function_space_specifier tMD_TypeDef* Heap_GetType(HEAP_PTR heapEntry);

function_space_specifier HEAP_PTR Heap_Box(tMD_TypeDef *pType, PTR pMem);
function_space_specifier HEAP_PTR Heap_Clone(HEAP_PTR obj);

function_space_specifier U32 Heap_SyncTryEnter(HEAP_PTR obj);
function_space_specifier U32 Heap_SyncExit(HEAP_PTR obj);

function_space_specifier HEAP_PTR Heap_SetWeakRefTarget(HEAP_PTR target, HEAP_PTR weakRef);
function_space_specifier HEAP_PTR* Heap_GetWeakRefAddress(HEAP_PTR target);
function_space_specifier void Heap_RemovedWeakRefTarget(HEAP_PTR target);
#endif