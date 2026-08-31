// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

use std::ffi::c_void;

use crate::com::{GUID, HRESULT, S_OK};

pub type ModuleID = usize;
pub type FunctionID = usize;
#[allow(non_camel_case_types)]
pub type mdMethodDef = u32;

macro_rules! define_stubs {
    ($( $ty:ident $name:ident ( $($arg:ty),* ) ),* $(,)?) => {
        $(
            pub type $ty = unsafe extern "system" fn(*mut c_void, $($arg),*) -> HRESULT;
            unsafe extern "system" fn $name(_this: *mut c_void, $(_: $arg),*) -> HRESULT {
                S_OK
            }
        )*
    };
}

define_stubs!(
    Stub0 stub0(),
    Stub1 stub1(usize),
    Stub2 stub2(usize, usize),
    Stub3 stub3(usize, usize, usize),
    Stub4 stub4(usize, usize, usize, usize),
    Stub5 stub5(usize, usize, usize, usize, usize),
    Stub7 stub7(usize, usize, usize, usize, usize, usize, usize),
    Stub9 stub9(usize, usize, usize, usize, usize, usize, usize, usize, usize),
);

pub type QueryInterfaceFn =
    unsafe extern "system" fn(*mut c_void, *const GUID, *mut *mut c_void) -> HRESULT;
pub type AddRefFn = unsafe extern "system" fn(*mut c_void) -> u32;
pub type ReleaseFn = unsafe extern "system" fn(*mut c_void) -> u32;
pub type InitializeFn = unsafe extern "system" fn(*mut c_void, *mut c_void) -> HRESULT;
pub type ShutdownFn = unsafe extern "system" fn(*mut c_void) -> HRESULT;
pub type ModuleLoadFinishedFn =
    unsafe extern "system" fn(*mut c_void, ModuleID, HRESULT) -> HRESULT;
pub type ModuleUnloadStartedFn = unsafe extern "system" fn(*mut c_void, ModuleID) -> HRESULT;
pub type GetReJITParametersFn =
    unsafe extern "system" fn(*mut c_void, ModuleID, mdMethodDef, *mut c_void) -> HRESULT;
pub type ReJITErrorFn =
    unsafe extern "system" fn(*mut c_void, ModuleID, mdMethodDef, FunctionID, HRESULT) -> HRESULT;

#[allow(non_snake_case)]
#[repr(C)]
pub struct CallbackVtable {
    // IUnknown
    pub QueryInterface: QueryInterfaceFn,
    pub AddRef: AddRefFn,
    pub Release: ReleaseFn,

    // ICorProfilerCallback
    pub Initialize: InitializeFn,
    pub Shutdown: ShutdownFn,
    pub AppDomainCreationStarted: Stub1,
    pub AppDomainCreationFinished: Stub2,
    pub AppDomainShutdownStarted: Stub1,
    pub AppDomainShutdownFinished: Stub2,
    pub AssemblyLoadStarted: Stub1,
    pub AssemblyLoadFinished: Stub2,
    pub AssemblyUnloadStarted: Stub1,
    pub AssemblyUnloadFinished: Stub2,
    pub ModuleLoadStarted: Stub1,
    pub ModuleLoadFinished: ModuleLoadFinishedFn,
    pub ModuleUnloadStarted: ModuleUnloadStartedFn,
    pub ModuleUnloadFinished: Stub2,
    pub ModuleAttachedToAssembly: Stub2,
    pub ClassLoadStarted: Stub1,
    pub ClassLoadFinished: Stub2,
    pub ClassUnloadStarted: Stub1,
    pub ClassUnloadFinished: Stub2,
    pub FunctionUnloadStarted: Stub1,
    pub JITCompilationStarted: Stub2,
    pub JITCompilationFinished: Stub3,
    pub JITCachedFunctionSearchStarted: Stub2,
    pub JITCachedFunctionSearchFinished: Stub2,
    pub JITFunctionPitched: Stub1,
    pub JITInlining: Stub3,
    pub ThreadCreated: Stub1,
    pub ThreadDestroyed: Stub1,
    pub ThreadAssignedToOSThread: Stub2,
    pub RemotingClientInvocationStarted: Stub0,
    pub RemotingClientSendingMessage: Stub2,
    pub RemotingClientReceivingReply: Stub2,
    pub RemotingClientInvocationFinished: Stub0,
    pub RemotingServerReceivingMessage: Stub2,
    pub RemotingServerInvocationStarted: Stub0,
    pub RemotingServerInvocationReturned: Stub0,
    pub RemotingServerSendingReply: Stub2,
    pub UnmanagedToManagedTransition: Stub2,
    pub ManagedToUnmanagedTransition: Stub2,
    pub RuntimeSuspendStarted: Stub1,
    pub RuntimeSuspendFinished: Stub0,
    pub RuntimeSuspendAborted: Stub0,
    pub RuntimeResumeStarted: Stub0,
    pub RuntimeResumeFinished: Stub0,
    pub RuntimeThreadSuspended: Stub1,
    pub RuntimeThreadResumed: Stub1,
    pub MovedReferences: Stub7,
    pub ObjectAllocated: Stub2,
    pub ObjectsAllocatedByClass: Stub5,
    pub ObjectReferences: Stub5,
    pub RootReferences: Stub3,
    pub ExceptionThrown: Stub1,
    pub ExceptionSearchFunctionEnter: Stub1,
    pub ExceptionSearchFunctionLeave: Stub0,
    pub ExceptionSearchFilterEnter: Stub1,
    pub ExceptionSearchFilterLeave: Stub0,
    pub ExceptionSearchCatcherFound: Stub1,
    pub ExceptionOSHandlerEnter: Stub1,
    pub ExceptionOSHandlerLeave: Stub1,
    pub ExceptionUnwindFunctionEnter: Stub1,
    pub ExceptionUnwindFunctionLeave: Stub0,
    pub ExceptionUnwindFinallyEnter: Stub1,
    pub ExceptionUnwindFinallyLeave: Stub0,
    pub ExceptionCatcherEnter: Stub2,
    pub ExceptionCatcherLeave: Stub0,
    pub COMClassicVTableCreated: Stub4,
    pub COMClassicVTableDestroyed: Stub3,
    pub ExceptionCLRCatcherFound: Stub0,
    pub ExceptionCLRCatcherExecute: Stub0,

    // ICorProfilerCallback2
    pub ThreadNameChanged: Stub4,
    pub GarbageCollectionStarted: Stub4,
    pub SurvivingReferences: Stub5,
    pub GarbageCollectionFinished: Stub0,
    pub FinalizeableObjectQueued: Stub2,
    pub RootReferences2: Stub9,
    pub HandleCreated: Stub2,
    pub HandleDestroyed: Stub1,

    // ICorProfilerCallback3
    pub InitializeForAttach: Stub3,
    pub ProfilerAttachComplete: Stub0,
    pub ProfilerDetachSucceeded: Stub0,

    // ICorProfilerCallback4
    pub ReJITCompilationStarted: Stub3,
    pub GetReJITParameters: GetReJITParametersFn,
    pub ReJITCompilationFinished: Stub4,
    pub ReJITError: ReJITErrorFn,
    pub MovedReferences2: Stub7,
    pub SurvivingReferences2: Stub5,
}

impl CallbackVtable {
    pub const fn all_stubs() -> Self {
        unsafe {
            Self {
                QueryInterface: std::mem::transmute::<Stub2, QueryInterfaceFn>(stub2),
                AddRef: std::mem::transmute::<Stub0, AddRefFn>(stub0),
                Release: std::mem::transmute::<Stub0, ReleaseFn>(stub0),
                Initialize: std::mem::transmute::<Stub1, InitializeFn>(stub1),
                Shutdown: stub0,
                AppDomainCreationStarted: stub1,
                AppDomainCreationFinished: stub2,
                AppDomainShutdownStarted: stub1,
                AppDomainShutdownFinished: stub2,
                AssemblyLoadStarted: stub1,
                AssemblyLoadFinished: stub2,
                AssemblyUnloadStarted: stub1,
                AssemblyUnloadFinished: stub2,
                ModuleLoadStarted: stub1,
                ModuleLoadFinished: std::mem::transmute::<Stub2, ModuleLoadFinishedFn>(stub2),
                ModuleUnloadStarted: stub1,
                ModuleUnloadFinished: stub2,
                ModuleAttachedToAssembly: stub2,
                ClassLoadStarted: stub1,
                ClassLoadFinished: stub2,
                ClassUnloadStarted: stub1,
                ClassUnloadFinished: stub2,
                FunctionUnloadStarted: stub1,
                JITCompilationStarted: stub2,
                JITCompilationFinished: stub3,
                JITCachedFunctionSearchStarted: stub2,
                JITCachedFunctionSearchFinished: stub2,
                JITFunctionPitched: stub1,
                JITInlining: stub3,
                ThreadCreated: stub1,
                ThreadDestroyed: stub1,
                ThreadAssignedToOSThread: stub2,
                RemotingClientInvocationStarted: stub0,
                RemotingClientSendingMessage: stub2,
                RemotingClientReceivingReply: stub2,
                RemotingClientInvocationFinished: stub0,
                RemotingServerReceivingMessage: stub2,
                RemotingServerInvocationStarted: stub0,
                RemotingServerInvocationReturned: stub0,
                RemotingServerSendingReply: stub2,
                UnmanagedToManagedTransition: stub2,
                ManagedToUnmanagedTransition: stub2,
                RuntimeSuspendStarted: stub1,
                RuntimeSuspendFinished: stub0,
                RuntimeSuspendAborted: stub0,
                RuntimeResumeStarted: stub0,
                RuntimeResumeFinished: stub0,
                RuntimeThreadSuspended: stub1,
                RuntimeThreadResumed: stub1,
                MovedReferences: stub7,
                ObjectAllocated: stub2,
                ObjectsAllocatedByClass: stub5,
                ObjectReferences: stub5,
                RootReferences: stub3,
                ExceptionThrown: stub1,
                ExceptionSearchFunctionEnter: stub1,
                ExceptionSearchFunctionLeave: stub0,
                ExceptionSearchFilterEnter: stub1,
                ExceptionSearchFilterLeave: stub0,
                ExceptionSearchCatcherFound: stub1,
                ExceptionOSHandlerEnter: stub1,
                ExceptionOSHandlerLeave: stub1,
                ExceptionUnwindFunctionEnter: stub1,
                ExceptionUnwindFunctionLeave: stub0,
                ExceptionUnwindFinallyEnter: stub1,
                ExceptionUnwindFinallyLeave: stub0,
                ExceptionCatcherEnter: stub2,
                ExceptionCatcherLeave: stub0,
                COMClassicVTableCreated: stub4,
                COMClassicVTableDestroyed: stub3,
                ExceptionCLRCatcherFound: stub0,
                ExceptionCLRCatcherExecute: stub0,
                ThreadNameChanged: stub4,
                GarbageCollectionStarted: stub4,
                SurvivingReferences: stub5,
                GarbageCollectionFinished: stub0,
                FinalizeableObjectQueued: stub2,
                RootReferences2: stub9,
                HandleCreated: stub2,
                HandleDestroyed: stub1,
                InitializeForAttach: stub3,
                ProfilerAttachComplete: stub0,
                ProfilerDetachSucceeded: stub0,
                ReJITCompilationStarted: stub3,
                GetReJITParameters: std::mem::transmute::<Stub3, GetReJITParametersFn>(stub3),
                ReJITCompilationFinished: stub4,
                ReJITError: std::mem::transmute::<Stub4, ReJITErrorFn>(stub4),
                MovedReferences2: stub7,
                SurvivingReferences2: stub5,
            }
        }
    }
}

const _: () = assert!(
    size_of::<CallbackVtable>() == 89 * size_of::<usize>(),
    "the callback vtable has the wrong number of slots"
);

macro_rules! callback_vtable {
    ($($field:ident : $value:expr),* $(,)?) => {
        $crate::vtable::CallbackVtable {
            $($field: $value,)*
            ..$crate::vtable::CallbackVtable::all_stubs()
        }
    };
}

pub(crate) use callback_vtable;

/// # Safety
///
/// `this` must be a live COM interface pointer, and `index` must be a slot that exists on it.
///
/// If `index` is wrong, this calls an unrelated method with the arguments of this one, which
/// is more likely to silently cause chaos than visibly fail at the callsite.
pub unsafe fn slot(this: *mut c_void, index: usize) -> *const c_void {
    unsafe {
        let vtable = *(this as *const *const *const c_void);
        *vtable.add(index)
    }
}

macro_rules! call_slot {
    ($this:expr, $index:expr, fn($($arg_ty:ty),* $(,)?) -> $ret:ty, ($($arg:expr),* $(,)?)) => {{
        let f: unsafe extern "system" fn(*mut c_void, $($arg_ty),*) -> $ret =
            unsafe { std::mem::transmute($crate::vtable::slot($this, $index)) };
        unsafe { f($this, $($arg),*) }
    }};
}

pub(crate) use call_slot;
